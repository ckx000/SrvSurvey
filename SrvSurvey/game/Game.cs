﻿using SrvSurvey.units;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace SrvSurvey.game
{
    /// <summary>
    /// Represents live state of the game Elite Dangerous
    /// </summary>
    class Game : IDisposable
    {
        static Game()
        {
            Game.logs = new List<string>();
            var releaseVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            Game.log($"SrvSurvey version: {releaseVersion}");

            settings = Settings.Load();
        }

        #region logging

        public static void log(object msg)
        {
            var txt = DateTime.Now.ToString("HH:mm:ss") + ": " + msg?.ToString();

            Debug.WriteLine(txt);

            Game.logs.Add(txt);

            ViewLogs.append(txt);
        }

        public static readonly List<string> logs;

        #endregion

        public static Game? activeGame { get; private set; }
        public static Settings settings { get; private set; }
        public bool initialized { get; private set; }

        /// <summary>
        /// The Commander actively playing the game
        /// </summary>
        public string? Commander { get; private set; }
        public bool isOdyssey { get; private set; }
        public string? starSystem { get; private set; }

        public JournalWatcher? journals;
        public Status? status { get; private set; }

        /// <summary>
        /// The name of the current start system, or body if we are close to one.
        /// </summary>
        public string? systemLocation { get; private set; }

        public Game(string? cmdr)
        {
            log($"Game .ctor");

            // track this instance as the active one
            Game.activeGame = this;

            if (!this.isRunning) return;

            // track status file changes and force an immediate read
            this.status = new Status(true);

            // initialize from a journal file
            var filepath = JournalFile.getCommanderJournalBefore(cmdr, DateTime.MaxValue);
            if (filepath == null)
            {
                Game.log($"No journal files found for: {cmdr}");
                this.isShutdown = true;
                return;
            }

            this.journals = new JournalWatcher(filepath);
            this.initializeFromJournal();
            if (this.isShutdown) return;

            log($"Cmdr loaded: {this.Commander != null}");

            // now listen for changes
            this.journals.onJournalEntry += Journals_onJournalEntry;
            this.status.StatusChanged += Status_StatusChanged;

            Status_StatusChanged();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (this.nearBody != null)
                {
                    this.nearBody.Dispose();
                    this.nearBody = null;

                }

                if (this.journals != null)
                {
                    this.journals.onJournalEntry -= Journals_onJournalEntry;
                    this.journals.Dispose();
                    this.journals = null;
                }

                if (this.status != null)
                {
                    this.status.StatusChanged -= Status_StatusChanged;
                    this.status.Dispose();
                    this.status = null;
                }
            }
        }

        #region modes

        public bool atMainMenu = false;
        private bool fsdJumping = false;
        public bool isShutdown = false;
        public LandableBody? nearBody;
        /// <summary>
        /// Tracks if we status had Lat/Long last time we knew
        /// </summary>
        private bool statusHasLatLong = false;
        /// <summary>
        /// Tracks BodyName from status - attempting to detect when multiple running games clobber the same file in error
        /// </summary>
        private string? statusBodyName;

        private void checkModeChange()
        {
            // check various things we actively need to know have changed
            if (this._mode != this.mode)
            {
                log($"Mode change {this._mode} => {this.mode}");
                this._mode = this.mode;

                if (modeChanged != null) modeChanged(this._mode);
                // fire event!
            }
        }

        private void Status_StatusChanged()
        {
            //log("status changed");
            if (this.statusBodyName != null && status!.BodyName != this.statusBodyName)
            {
                // exit early if the body suddenly switches to something different without being NULL first
                Game.log($"Multiple running games? this.statusBodyName: ${this.statusBodyName} vs status.BodyName: {status.BodyName}");
                // TODO: some planet/moons are close enough together to trigger this - maybe compare the first word from each?
                return;
            }
            this.statusBodyName = status!.BodyName;

            // track if status HasLatLong, toggling behaviours when it changes
            var newHasLatLong = (this.status.Flags & StatusFlags.HasLatLong) > 0;
            if (this.statusHasLatLong != newHasLatLong)
            {
                Game.log($"Changed hasLatLong: {newHasLatLong}, has nearBody: {this.nearBody != null}, nearBody matches: {status.BodyName == this.nearBody?.bodyName}");
                this.statusHasLatLong = newHasLatLong;

                if (!newHasLatLong)
                {
                    // we are departing
                    if (this.departingBody != null && this.nearBody != null)
                    {
                        Game.log($"EVENT:departingBody, from {this.nearBody.bodyName}");
                        // change systemLocation to be just system (not the body)
                        this.systemLocation = this.starSystem;

                        this.departingBody(this.nearBody);
                        this.nearBody = null;
                    }
                }
                else
                {
                    if (this.nearBody == null)
                    {
                        // we are approaching - create and fire event
                        this.createNearBody(status.BodyName);
                    }
                }
            }

            this.checkModeChange();

        }

        public event GameNearingBody? nearingBody;
        public event GameDepartingBody? departingBody;

        public event GameModeChanged? modeChanged;
        private GameMode _mode;
        public GameMode mode
        {
            get
            {
                if (!this.isRunning || this.isShutdown)
                    return GameMode.Offline;

                if (this.atMainMenu || this.Commander == null)
                    return GameMode.MainMenu;

                // use GuiFocus if it is interesting
                if (this.status?.GuiFocus != GuiFocus.NoFocus)
                    return (GameMode)(int)this.status!.GuiFocus;

                if (this.fsdJumping)
                    return GameMode.FSDJumping;

                // otherwise use the type of vehicle we are in
                var activeVehicle = this.vehicle;
                if (activeVehicle == ActiveVehicle.Fighter)
                    return GameMode.InFighter;
                if (activeVehicle == ActiveVehicle.SRV)
                    return GameMode.InSrv;
                if (activeVehicle == ActiveVehicle.Taxi)
                    return GameMode.InTaxi;
                if ((this.status.Flags2 & StatusFlags2.OnFootExterior) > 0)
                    return GameMode.OnFoot;

                // otherwise we are in the main ship, travelling ...
                if ((this.status.Flags & StatusFlags.Supercruise) > 0)
                    return GameMode.SuperCruising;
                if ((this.status.Flags2 & StatusFlags2.GlideMode) > 0)
                    return GameMode.GlideMode;

                if ((this.status.Flags & StatusFlags.Landed) > 0)
                    return GameMode.Landed;

                // or we maybe ...
                if ((this.status.Flags & StatusFlags.Docked) > 0)
                    return GameMode.Docked;
                if ((this.status.Flags2 & StatusFlags2.OnFootInStation) > 0)
                    return GameMode.Docked;

                // otherwise we must be ...
                if (activeVehicle == ActiveVehicle.MainShip)
                    return GameMode.Flying;

                Game.log($"Unknown game mode? status.Flags: {status.Flags}, status.Flags2: {status.Flags2}");
                return GameMode.Unknown;
            }
        }

        public bool isMode(params GameMode[] modes)
        {
            return modes.Contains(this.mode);
        }

        public ActiveVehicle vehicle
        {
            get
            {
                if (this.status == null)
                    return ActiveVehicle.Unknown;
                else if ((this.status.Flags & StatusFlags.InMainShip) > 0)
                    return ActiveVehicle.MainShip;
                else if ((this.status.Flags & StatusFlags.InFighter) > 0)
                    return ActiveVehicle.Fighter;
                else if ((this.status.Flags & StatusFlags.InSRV) > 0)
                    return ActiveVehicle.SRV;
                else if ((this.status.Flags2 & StatusFlags2.OnFoot) > 0)
                    return ActiveVehicle.Foot;
                else if ((this.status.Flags2 & StatusFlags2.InTaxi) > 0)
                    return ActiveVehicle.Taxi;
                else
                    //return ActiveVehicle.Docked;
                    return ActiveVehicle.Unknown;

                // TODO: do we care about telepresence?
            }
        }

        public bool isLanded
        {
            get => this.mode == GameMode.InSrv || this.mode == GameMode.OnFoot || this.mode == GameMode.Landed;
        }

        public bool showBodyPlotters
        {
            get => !this.isShutdown
                && !this.atMainMenu
                && this.isMode(GameMode.SuperCruising, GameMode.Flying, GameMode.Landed, GameMode.InSrv, GameMode.OnFoot, GameMode.GlideMode);
        }

        public bool showGuardianPlotters
        {
            get => showBodyPlotters
                && this.nearBody != null
                && this.touchdownLocation != null
                && !string.IsNullOrEmpty(this.nearBody.guardianSiteName);
        }

        #endregion

        #region Process and window handle stuff

        private static IntPtr getGameWindowHandle()
        {
            //this.TopMost = !this.TopMost;
            var procED = Process.GetProcessesByName("EliteDangerous64");
            if (procED.Length > 0)
                return procED[0].MainWindowHandle;
            else
                return IntPtr.Zero;
        }

        /// <summary>
        /// Returns True if the game is actively running
        /// </summary>
        public bool isRunning
        {
            get
            {
                var procED = Process.GetProcessesByName("EliteDangerous64");
                return procED.Length > 0;
            }
        }

        /// <summary>
        /// Returns True if multiple versions of the game are running
        /// </summary>
        public bool isRunningMultiple
        {
            get
            {
                var procED = Process.GetProcessesByName("EliteDangerous64");
                return procED.Length > 1;
            }
        }

        #endregion

        #region start-up / initialization from journals

        public static string journalFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"Saved Games\Frontier Developments\Elite Dangerous\");

        public string? getLastJournalBefore(string cmdr, DateTime timestamp)
        {
            var manyFiles = new DirectoryInfo(SrvSurvey.journalFolder)
                .EnumerateFiles("*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(_ => _.LastWriteTimeUtc);

            var journalFiles = manyFiles
                .Where(_ => _.LastWriteTime < timestamp)
                .Select(_ => _.FullName);

            if (cmdr == null)
            {
                // use the most recent journal file
                return journalFiles.First();
            }

            var CMDR = cmdr.ToUpper();
            //var filename = journalFiles.First((filepath) => isJournalForCmdr(cmdr, filepath));

            var filename = journalFiles.FirstOrDefault((filepath) =>
            {
                // TODO: Use some streaming reader to save reading the whole file up front?
                using (var reader = new StreamReader(new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine()!;

                        // TODO: allow for non-Odyssey
                        if (line.Contains("\"event\":\"Fileheader\"") && line.Contains("\"Odyssey\":false"))
                            Game.log($"Reading non-Odyssey journal!");

                        if (line.Contains("\"event\":\"Commander\""))
                            // no need to process further lines
                            return line.ToUpper().Contains($"\"NAME\":\"{CMDR}\"");
                    }
                    return false;
                }
            });

            // TODO: As we already loaded the journal into memory, it would be nice to use that rather than reload it again from JournalWatcher

            return filename;
        }


        private void initializeFromJournal()
        {
            log($"Game.initializeFromJournal: BEGIN {this.Commander}, starSystem:{this.starSystem}, systemLocation:{this.systemLocation}, nearBody:{this.nearBody}, journals.Count:{journals?.Count}");

            var loadEntry = this.journals!.FindEntryByType<LoadGame>(0, false);

            // read cmdr info
            if (this.Commander == null && loadEntry != null)
                this.Commander = loadEntry.Commander;

            // if we are not shutdown ...
            var lastShutdown = journals.FindEntryByType<Shutdown>(-1, true);
            if (lastShutdown == null)
            {
                // ... and we have MainMenu music - we know we're not actively playing
                var lastMusic = journals.FindEntryByType<Music>(-1, true);
                if (lastMusic != null)
                    onJournalEntry(lastMusic);
            }
            else
            {
                log($"Game.initializeFromJournal: EXIT isShutdown! ({Game.activeGame == this})");
                this.isShutdown = true;
                return;
            }

            // if we have landed, we need to find the last Touchdown location
            if (this.isLanded && this._touchdownLocation == null) // || (status.Flags & StatusFlags.HasLatLong) > 0)
            {
                journals.searchDeep(
                    (Touchdown lastTouchdown) =>
                    {
                        Game.log($"LastTouchdown: {lastTouchdown}");

                        if (lastTouchdown.Body == status!.BodyName)
                        {
                            onJournalEntry(lastTouchdown);
                            return true;
                        }
                        return false;
                    }
                );
            }

            // if we are near a planet
            if (this.nearBody == null && (status!.Flags & StatusFlags.HasLatLong) > 0)
            {
                this.createNearBody(status.BodyName);
            }

            if (this.systemLocation == null)
            {
                var locationEntry = journals.FindEntryByType<Location>(-1, true);
                if (locationEntry != null)
                {
                    this.starSystem = locationEntry.StarSystem;
                    this.systemLocation = Util.getLocationString(locationEntry.StarSystem, locationEntry.Body);
                }
            }
            log($"Game.initializeFromJournal: END Commander:{this.Commander}, starSystem:{this.starSystem}, systemLocation:{this.systemLocation}, nearBody:{this.nearBody}, journals.Count:{journals.Count}");

            this.initialized = Game.activeGame == this && this.Commander != null;
            this.checkModeChange();
        }


        #endregion

        private void createNearBody(string bodyName)
        {
            // exit early if we already have that body
            if (this.nearBody != null && this.nearBody.bodyName == bodyName) return;

            if (this.nearBody == null)
            {
                // look for a recent scan event
                journals!.search((Scan scan) =>
                {
                    if (scan.Bodyname == bodyName)
                    {
                        this.nearBody = new LandableBody(
                            this,
                            scan.Bodyname,
                            scan.BodyID,
                            scan.SystemAddress)
                        {
                            radius = scan.Radius,
                        };
                        this.starSystem = scan.StarSystem;
                        this.systemLocation = Util.getLocationString(scan.StarSystem, scan.Bodyname);
                        return true;
                    }
                    return false;
                });
            }

            if (this.nearBody == null)
            {
                // look for a recent ApproachBody event?
                journals!.search((ApproachBody approachBody) =>
                {
                    if (approachBody.Body == bodyName && status!.BodyName == approachBody.Body && status.PlanetRadius > 0)
                    {
                        this.nearBody = new LandableBody(
                            this,
                            approachBody.Body,
                            approachBody.BodyID,
                            approachBody.SystemAddress)
                        {
                            radius = status.PlanetRadius,
                        };
                        this.starSystem = approachBody.StarSystem;
                        this.systemLocation = Util.getLocationString(approachBody.StarSystem, approachBody.Body);
                        return true;
                    }
                    return false;
                });
            }

            // look for recent Location event?
            if (this.nearBody == null)
            {
                journals!.search((Location locationEntry) =>
                {
                    if (locationEntry.Body == bodyName && status!.BodyName == locationEntry.Body && status.PlanetRadius > 0)
                    {
                        this.nearBody = new LandableBody(
                            this,
                            locationEntry.Body,
                            locationEntry.BodyID,
                            locationEntry.SystemAddress)
                        {
                            radius = status.PlanetRadius,
                        };
                        this.starSystem = locationEntry.StarSystem;
                        this.systemLocation = Util.getLocationString(locationEntry.StarSystem, locationEntry.Body);
                        return true;
                    }
                    return false;
                });
            }

            // look for recent SupercruiseExit event?
            if (this.nearBody == null)
            {
                journals!.search((SupercruiseExit exitEvent) =>
                {
                    if (exitEvent.Body == bodyName && status!.BodyName == exitEvent.Body && status.PlanetRadius > 0)
                    {
                        this.nearBody = new LandableBody(
                            this,
                            exitEvent.Body,
                            exitEvent.BodyID,
                            exitEvent.SystemAddress)
                        {
                            radius = status.PlanetRadius,
                        };
                        this.starSystem = exitEvent.Starsystem;
                        this.systemLocation = Util.getLocationString(exitEvent.Starsystem, exitEvent.Body);
                        return true;
                    }
                    return false;
                });
            }

            if (this.nearBody?.Genuses != null)
            {
                log($"Genuses" + string.Join(",", this.nearBody.Genuses.Select(_ => _.Genus_Localised)));
            }

            if (this.systemLocation == null && this.nearBody != null && status!.BodyName == this.nearBody.bodyName)
            {
                Game.log("createNearBody: add systemLocation needed here?");
                //this.systemLocation = Util.getLocationString( nearBody.systemAddress, entry.Body) this.nearBody?.bodyName;
            }

            if (this.nearBody?.guardianSiteCount > 0)
            {
                // do we have an ApproachSettlement for this site?
                this.journals!.searchDeep<ApproachSettlement>(
                    (ApproachSettlement _) =>
                    {
                        if (_.BodyName == status?.BodyName)
                        {
                            this.nearBody.onJournalEntry(_);
                            return true;
                        }
                        return false;
                    },
                    (JournalFile journals) =>
                    {
                        // stop searching older journal files if we see we approached this body
                        return journals.search((ApproachBody _) => true);
                    });
            }

            if (this.nearBody != null)
            {
                Game.log($"EVENT:nearingBody, at {this.nearBody?.bodyName}");

                if (this.nearingBody != null && this.nearBody != null)
                    this.nearingBody(this.nearBody);
            }
        }

        #region journal tracking for game state and modes

        private void Journals_onJournalEntry(JournalEntry entry, int index)
        {
            Game.log($"Game.event => {entry.@event}");
            this.onJournalEntry((dynamic)entry);
        }

        private void onJournalEntry(JournalEntry entry) { /* ignore */ }

        private void onJournalEntry(LoadGame entry)
        {
            if (this.Commander == null)
                this.initializeFromJournal();

            this.checkModeChange();
        }

        private void onJournalEntry(Location entry)
        {
            this.starSystem = entry.StarSystem;
        }

        private void onJournalEntry(Music entry)
        {
            var newMainMenu = entry.MusicTrack == "MainMenu";
            if (this.atMainMenu != newMainMenu)
            {
                // if atMainMenu has changed - force a status change event
                this.atMainMenu = newMainMenu;
                this.Status_StatusChanged();
            }
        }

        private void onJournalEntry(Shutdown entry)
        {
            this.atMainMenu = false;
            this.isShutdown = true;
            this.checkModeChange();
            this.Status_StatusChanged();
        }

        private void onJournalEntry(StartJump entry)
        {
            // FSD charged - either for Jump or SuperCruise
            if (entry.JumpType == "Hyperspace")
            {
                this.fsdJumping = true;
                this.checkModeChange();
            }
        }

        private void onJournalEntry(FSDJump entry)
        {
            // FSD Jump completed
            this.fsdJumping = false;
            this.starSystem = entry.StarSystem;
            this.systemLocation = Util.getLocationString(entry.StarSystem, entry.Body);
            this.statusBodyName = null;
            this.checkModeChange();
        }

        private void onJournalEntry(SupercruiseExit entry)
        {
            this.systemLocation = Util.getLocationString(entry.Starsystem, entry.Body);
        }

        #endregion

        #region journal tracking for ground ops

        private LatLong2? _touchdownLocation;

        public LatLong2 touchdownLocation
        {
            get
            {
                if (_touchdownLocation == null)
                {
                    Game.log($"Searching journals for last touchdown location...");
                    var lastTouchdown = journals!.FindEntryByType<Touchdown>(-1, true);
                    var lastLiftoff = journals.FindEntryByType<Liftoff>(-1, true);
                    if (lastTouchdown != null)
                    {
                        if (lastLiftoff == null || lastTouchdown.timestamp > lastLiftoff.timestamp)
                        {
                            _touchdownLocation = new LatLong2(lastTouchdown);
                        }
                        //else
                        //{
                        //    _touchdownLocation = new LatLong2(lastTouchdown);
                        //}
                    }
                }
                return _touchdownLocation!;
            }
        }

        private void onJournalEntry(Touchdown entry)
        {
            this._touchdownLocation = new LatLong2(entry);

            var ago = Util.timeSpanToString(DateTime.UtcNow - entry.timestamp);
            log($"Touchdown {ago}, at: {touchdownLocation}");
        }

        private void onJournalEntry(Liftoff entry)
        {
            this._touchdownLocation = null;
            log($"Liftoff!");
        }

        private void onJournalEntry(ApproachBody entry)
        {
            if (this.nearBody == null)
                this.createNearBody(entry.Body);
        }

        private void onJournalEntry(SAASignalsFound entry)
        {
            if (this.nearBody == null)
                this.createNearBody(entry.BodyName);
            else if (this.nearBody.Genuses == null)
                this.nearBody.readSAASignalsFound(entry);
        }

        #endregion
    }
}
