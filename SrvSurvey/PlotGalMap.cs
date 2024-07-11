﻿using SrvSurvey.game;
using System.Drawing.Drawing2D;

namespace SrvSurvey
{
    internal class PlotGalMap : PlotBase, PlotterForm
    {
        private static bool smaller = true; // temp?
        private static GraphicsPath triangle;

        static PlotGalMap()
        {
            triangle = new GraphicsPath();
            triangle.AddPolygon(new Point[] {
                    scaled(new Point(0, 0)),
                    scaled(new Point(0, smaller ? 20 : 36)),
                    scaled(new Point(smaller ? 8 : 12,  smaller ? 10 : 18)),
                });
        }

        private List<RouteInfo> hops = new List<RouteInfo>();
        private double distanceJumped;

        private PlotGalMap() : base()
        {
            this.Size = Size.Empty;
            this.Font = GameColors.fontSmall2;
        }

        protected override void OnLoad(EventArgs e)
        {
            this.MinimumSize = new Size(scaled(96), scaled(44));
            base.OnLoad(e);

            this.initialize();

            this.reposition(Elite.getWindowRect(true));
            this.onJournalEntry(new NavRoute());
        }

        public static bool allowPlotter
        {
            get => Game.activeGame != null
                && Game.activeGame.mode == GameMode.GalaxyMap
                && Game.settings.useExternalData;
        }

        public override void reposition(Rectangle gameRect)
        {
            if (gameRect == Rectangle.Empty)
            {
                this.Opacity = 0;
                return;
            }


            this.Opacity = PlotPos.getOpacity(this);
            PlotPos.reposition(this, gameRect);
            this.Invalidate();
        }

        protected override void Game_modeChanged(GameMode newMode, bool force)
        {
            if (this.IsDisposed) return;

            var showPlotter = PlotGalMap.allowPlotter;
            if (this.Opacity > 0 && !showPlotter)
                Program.closePlotter<PlotGalMap>();
            else if (this.Opacity == 0 && showPlotter)
                this.reposition(Elite.getWindowRect());
        }

        protected override void onJournalEntry(NavRoute entry)
        {
            if (this.IsDisposed) return;
            this.hops.Clear();

            // lookup if target system has been discovered
            if (game.navRoute.Route.Count < 2)
                return;

            // the desintation is last
            this.hops.Add(RouteInfo.create(game.navRoute.Route.Last(), true));


            var next = game.fsdTarget != null
                ? game.navRoute.Route.Find(_ => _.StarSystem == game.fsdTarget) ?? game.navRoute.Route[1]
                : game.navRoute.Route[1];

            if (game.navRoute.Route.Count > 2 && next.StarSystem != hops.FirstOrDefault()?.systemName)
                this.hops.Add(RouteInfo.create(next, false));

            this.distanceJumped = 0;
            for (int n = 1; n < game.navRoute.Route.Count; n++)
            {
                var d = Util.getSystemDistance(game.navRoute.Route[n - 1].StarPos, game.navRoute.Route[n].StarPos);
                this.distanceJumped += d;
            }

            //var target = game.navRoute.Route.LastOrDefault()?.StarSystem;
            //this.hops.Add(new RouteInfo(target, this));

            //var next = game.navRoute.Route.Count == 0 ? null : game.navRoute.Route[1].StarSystem;
            //this.hops.Add(new RouteInfo(next, this));

            //if (target != null)
            //    this.lookupSystem(target, false);
            //else
            //{
            //    this.targetSystem = null;
            //    this.targetStatus = null;
            //    this.targetSubStatus = null;
            //}

            //if (next != null && target != next)
            //    this.lookupSystem(next, true);
            //else
            //{
            //    this.nextSystem = null;
            //    this.nextStatus = null;
            //    this.nextSubStatus = null;
            //}
        }

        protected override void onJournalEntry(NavRouteClear entry)
        {
            if (this.IsDisposed) return;
            this.hops.Clear();

            this.Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            if (this.IsDisposed) return;
            try
            {
                this.resetPlotter(g);

                if (this.hops.Count == 0)
                {
                    this.drawTextAt(eight, $"No route set");
                    this.newLine(+ten, true);
                }
                else
                {
                    foreach (var hop in this.hops)
                        drawSystemSummary(hop);// "Next jump", nextSystem, nextStatus, nextSubStatus);


                    this.drawTextAt(eight, $"Total jumps: {game.navRoute.Route.Count - 1} ► Distance: {this.distanceJumped.ToString("N1")} ly", GameColors.brushGameOrange);
                    this.newLine(true);

                    this.drawTextAt(eight, $"Data from: edsm.net + spansh.co.uk", GameColors.brushGameOrangeDim);
                    this.newLine(true);
                }

                this.formAdjustSize(+ten, +ten);
            }
            catch (Exception ex)
            {
                Game.log($"PlotGalMap.OnPaintBackground error: {ex}");
            }
        }

        private void drawSystemSummary(RouteInfo hop)
        {
            if (hop.destination)
                this.drawTextAt(eight, $"Destination: ");
            else
                this.drawTextAt(eight, $"Next jump:");

            // line 1: system name
            this.drawTextAt(eightSix, $"► {hop.systemName}", GameColors.fontSmall2Bold);
            this.newLine(true);

            // line 2: status
            if (hop.highlight)
                this.drawTextAt(eightSix, $"{hop.status}", GameColors.brushCyan, GameColors.fontSmall2Bold);
            else
                this.drawTextAt(eightSix, $"{hop.status}");

            // line 3: who discovered
            if (!string.IsNullOrEmpty(hop.subStatus))
            {
                this.newLine(true);
                this.drawTextAt(eightSix, $"{hop.subStatus}");
            }

            // line 4: bio signals?
            if (hop.sumGenus > 0)
            {
                this.newLine(true);
                this.drawTextAt(eightSix, $"{hop.sumGenus}x Genus", GameColors.brushCyan, GameColors.fontSmall2Bold);
            }

            this.newLine(+ten, true);
        }
    }

    class RouteInfo
    {
        private static Dictionary<double, RouteInfo> cache = new Dictionary<double, RouteInfo>();

        public static RouteInfo create(RouteEntry entry, bool destination)
        {
            var info = cache.GetValueOrDefault(entry.SystemAddress);

            if (info == null)
            {
                info = new RouteInfo(entry, destination);
                cache.Add(entry.SystemAddress, info);
            }

            info.destination = destination;
            return info;
        }

        public RouteEntry entry;
        public string status = "...";
        public string? subStatus;
        public string? bio;
        public bool highlight;
        public bool destination;
        public int sumGenus;

        public RouteInfo(RouteEntry entry, bool destination)
        {
            this.entry = entry;
            this.destination = destination;

            this.lookupSystem();
        }

        public string systemName { get => entry.StarSystem; }

        private void lookupSystem()
        {
            // lookup in EDSM
            Game.edsm.getBodies(systemName).ContinueWith(result => Program.crashGuard(() =>
            {
                if (result.Exception != null)
                {
                    Util.isFirewallProblem(result.Exception);
                    return;
                }
                var edsmResult = result.Result;

                if (edsmResult.name == null || edsmResult.id64 == 0)
                {
                    // system is not known to EDSM
                    status = "Undiscovered system";
                    highlight = true;
                }
                else if (edsmResult.bodyCount == 0)
                {
                    // system is known from routes but it has not been visited
                    status = "Unvisited system";
                    highlight = true;
                }
                else
                {
                    if (edsmResult.bodyCount == edsmResult.bodies.Count)
                        status = $"Discovered, {edsmResult.bodyCount} bodies";
                    else
                        status = $"Discovered ({edsmResult.bodies.Count} of {edsmResult.bodyCount} bodies)";

                    var discCmdr = edsmResult.bodies.FirstOrDefault()?.discovery?.commander;
                    var discDate = edsmResult.bodies.FirstOrDefault()?.discovery?.date.ToShortDateString();
                    if (discCmdr != null && discDate != null)
                        subStatus = $"By {discCmdr}, {discDate}";
                }

                var plotter = Program.getPlotter<PlotGalMap>();
                if (plotter != null && plotter.Created)
                    plotter.BeginInvoke(() => plotter.Invalidate());

            }));

            Game.spansh.getSystemDump((long)entry.SystemAddress).ContinueWith(result => Program.crashGuard(() =>
            {
                if (result.Exception != null)
                {
                    Util.isFirewallProblem(result.Exception);
                    return;
                }
                var spanshResult = result.Result;
                this.sumGenus = spanshResult.bodies.Sum(_ => _.signals?.genuses?.Count ?? 0);

            }));

            // TODO: maybe lookup in Canonn for bio data too?

        }

    }

}
