/*
Copyright (c) 2013, Lars Brubaker

This file is part of MatterSlice.

MatterSlice is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

MatterSlice is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with MatterSlice.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.IO;
using System.Collections.Generic;

using ClipperLib;

namespace MatterHackers.MatterSlice
{
    using Polygon = List<IntPoint>;
    using Polygons = List<List<IntPoint>>;

    public class GCodeExport
    {
        StreamWriter f;
        double extrusionAmount;
        double extrusionPerMM;
        double retractionAmount;
        double extruderSwitchRetraction;
        double minimalExtrusionBeforeRetraction;
        double extrusionAmountAtPreviousRetraction;
        Point3 currentPosition;
        IntPoint[] extruderOffset = new IntPoint[ConfigConstants.MAX_EXTRUDERS];
        int currentSpeed, retractionSpeed;
        int zPos;
        bool isRetracted;
        int extruderNr;
        int currentFanSpeed;
        int flavor;

        double[] totalFilament = new double[ConfigConstants.MAX_EXTRUDERS];
        double totalPrintTime;
        TimeEstimateCalculator estimateCalculator = new TimeEstimateCalculator();

        public GCodeExport()
        {
            extrusionAmount = 0;
            extrusionPerMM = 0;
            retractionAmount = 4.5;
            minimalExtrusionBeforeRetraction = 0.0;
            extrusionAmountAtPreviousRetraction = -10000;
            extruderSwitchRetraction = 14.5;
            extruderNr = 0;
            currentFanSpeed = -1;

            totalPrintTime = 0.0;
            for (int e = 0; e < ConfigConstants.MAX_EXTRUDERS; e++)
                totalFilament[e] = 0.0;

            currentSpeed = 0;
            retractionSpeed = 45;
            isRetracted = true;
            f = new StreamWriter(Console.OpenStandardOutput());
        }

        public void Close()
        {
            f.Close();
        }

        public void replaceTagInStart(string tag, string replaceValue)
        {
            throw new NotImplementedException();
#if false
    long oldPos = ftello64(f);
    
    char[] buffer = new char[1024];
    fseeko64(f, 0, SEEK_SET);
    fread(buffer, 1024, 1, f);
    
    char* c = strstr(buffer, tag);
    memset(c, ' ', strlen(tag));
    if (c) memcpy(c, replaceValue, strlen(replaceValue));
    
    fseeko64(f, 0, SEEK_SET);
    fwrite(buffer, 1024, 1, f);
    
    fseeko64(f, oldPos, SEEK_SET);
#endif
        }

        public void setExtruderOffset(int id, IntPoint p)
        {
            extruderOffset[id] = p;
        }

        public void setFlavor(int flavor)
        {
            this.flavor = flavor;
        }

        public int getFlavor()
        {
            return this.flavor;
        }

        public void setFilename(string filename)
        {
            filename = filename.Replace("\"", "");
            f = new StreamWriter(filename);
        }

        public bool isValid()
        {
            return f != null;
        }

        public void setExtrusion(int layerThickness, int filamentDiameter, int flow)
        {
            double filamentArea = Math.PI * ((double)(filamentDiameter) / 1000.0 / 2.0) * ((double)(filamentDiameter) / 1000.0 / 2.0);
            if (flavor == ConfigConstants.GCODE_FLAVOR_ULTIGCODE)//UltiGCode uses volume extrusion as E value, and thus does not need the filamentArea in the mix.
                extrusionPerMM = (double)(layerThickness) / 1000.0;
            else
                extrusionPerMM = (double)(layerThickness) / 1000.0 / filamentArea * (double)(flow) / 100.0;
        }

        public void setRetractionSettings(int retractionAmount, int retractionSpeed, int extruderSwitchRetraction, int minimalExtrusionBeforeRetraction)
        {
            this.retractionAmount = (double)(retractionAmount) / 1000.0;
            this.retractionSpeed = retractionSpeed;
            this.extruderSwitchRetraction = (double)(extruderSwitchRetraction) / 1000.0;
            this.minimalExtrusionBeforeRetraction = (double)(minimalExtrusionBeforeRetraction) / 1000.0;
        }

        public void setZ(int z)
        {
            this.zPos = z;
        }

        public IntPoint getPositionXY()
        {
            return new IntPoint(currentPosition.x, currentPosition.y);
        }

        public int getPositionZ()
        {
            return currentPosition.z;
        }

        public int getExtruderNr()
        {
            return extruderNr;
        }

        public double getTotalFilamentUsed(int e)
        {
            if (e == extruderNr)
            {
                return totalFilament[e] + extrusionAmount;
            }

            return totalFilament[e];
        }

        public double getTotalPrintTime()
        {
            return totalPrintTime;
        }

        public void updateTotalPrintTime()
        {
            totalPrintTime += estimateCalculator.calculate();
            estimateCalculator.reset();
        }

        public void addComment(string comment)
        {
            f.Write(";{0}\n".FormatWith(comment));
        }

        public void addLine(string line)
        {
            f.Write(";{0}\n".FormatWith(line));
        }

        public void resetExtrusionValue()
        {
            if (extrusionAmount != 0.0)
            {
                f.Write("G92 E0\n");
                totalFilament[extruderNr] += extrusionAmount;
                extrusionAmountAtPreviousRetraction -= extrusionAmount;
                extrusionAmount = 0.0;
            }
        }

        public void addDelay(double timeAmount)
        {
            f.Write("G4 P{0}\n".FormatWith((int)(timeAmount * 1000)));
            totalPrintTime += timeAmount;
        }

        public void addMove(IntPoint p, int speed, int lineWidth)
        {
            if (lineWidth != 0)
            {
                IntPoint diff = p - getPositionXY();
                if (isRetracted)
                {
                    if (flavor == ConfigConstants.GCODE_FLAVOR_ULTIGCODE)
                    {
                        f.Write("G11\n");
                    }
                    else
                    {
                        f.Write("G1 F{0} E{0:0.00000}\n".FormatWith(retractionSpeed * 60, extrusionAmount));
                        currentSpeed = retractionSpeed;
                        estimateCalculator.plan(new TimeEstimateCalculator.Position((double)(p.X) / 1000.0, (p.Y) / 1000.0, (double)(zPos) / 1000.0, extrusionAmount), currentSpeed);
                    }
                    if (extrusionAmount > 10000.0) // Having more then 21m of extrusion causes inaccuracies. So reset it every 10m, just to be sure.
                        resetExtrusionValue();
                    isRetracted = false;
                }
                extrusionAmount += extrusionPerMM * (double)(lineWidth) / 1000.0 * diff.vSizeMM();
                f.Write("G1");
            }
            else
            {
                f.Write("G0");
            }

            if (currentSpeed != speed)
            {
                f.Write(" F{0}".FormatWith(speed * 60));
                currentSpeed = speed;
            }
            f.Write(" X{0:0.00} Y{1:0.00}".FormatWith((float)(p.X - extruderOffset[extruderNr].X) / 1000, (float)(p.Y - extruderOffset[extruderNr].Y) / 1000));
            if (zPos != currentPosition.z)
                f.Write(" Z{0:0.00}".FormatWith((float)(zPos) / 1000));
            if (lineWidth != 0)
                f.Write(" E{0:0.00000}".FormatWith(extrusionAmount));
            f.Write("\n");

            currentPosition = new Point3(p.X, p.Y, zPos);
            estimateCalculator.plan(new TimeEstimateCalculator.Position((double)(currentPosition.x) / 1000.0, (currentPosition.y) / 1000.0, (double)(currentPosition.z) / 1000.0, extrusionAmount), currentSpeed);
        }

        public void addRetraction()
        {
            if (retractionAmount > 0 && !isRetracted && extrusionAmountAtPreviousRetraction + minimalExtrusionBeforeRetraction < extrusionAmount)
            {
                if (flavor == ConfigConstants.GCODE_FLAVOR_ULTIGCODE)
                {
                    f.Write("G10\n");
                }
                else
                {
                    f.Write("G1 F{0} E{1:0.00000}\n".FormatWith(retractionSpeed * 60, extrusionAmount - retractionAmount));
                    currentSpeed = retractionSpeed;
                    estimateCalculator.plan(new TimeEstimateCalculator.Position((double)(currentPosition.x) / 1000.0, (currentPosition.y) / 1000.0, (double)(currentPosition.z) / 1000.0, extrusionAmount - retractionAmount), currentSpeed);
                }
                extrusionAmountAtPreviousRetraction = extrusionAmount;
                isRetracted = true;
            }
        }

        public void switchExtruder(int newExtruder)
        {
            if (extruderNr == newExtruder)
                return;

            resetExtrusionValue();
            extruderNr = newExtruder;

            if (flavor == ConfigConstants.GCODE_FLAVOR_ULTIGCODE)
            {
                f.Write("G10 S1\n");
            }
            else
            {
                f.Write("G1 F{0} E{1:0.0000}\n", retractionSpeed * 60, extrusionAmount - extruderSwitchRetraction);
                currentSpeed = retractionSpeed;
            }
            isRetracted = true;
            if (flavor == ConfigConstants.GCODE_FLAVOR_MAKERBOT)
                f.Write("M135 T{0}\n".FormatWith(extruderNr));
            else
                f.Write("T{0}\n".FormatWith(extruderNr));
        }

        public void addCode(string str)
        {
            f.Write("{0}\n".FormatWith(str));
        }

        public void addFanCommand(int speed)
        {
            if (currentFanSpeed == speed)
                return;
            if (speed > 0)
            {
                if (flavor == ConfigConstants.GCODE_FLAVOR_MAKERBOT)
                    f.Write("M126 T0 ; value = {0}\n".FormatWith(speed * 255 / 100));
                else
                    f.Write("M106 S{0}\n".FormatWith(speed * 255 / 100));
            }
            else
            {
                if (flavor == ConfigConstants.GCODE_FLAVOR_MAKERBOT)
                    f.Write("M127 T0\n");
                else
                    f.Write("M107\n");
            }
            currentFanSpeed = speed;
        }

        public long getFileSize()
        {
            return f.BaseStream.Length;
        }

        public void tellFileSize()
        {
            double fsize = f.BaseStream.Length;
            if (fsize > 1024 * 1024)
            {
                fsize /= 1024.0 * 1024.0;
                LogOutput.log("Wrote {0:0.0} MB.\n".FormatWith(fsize));
            }
            if (fsize > 1024)
            {
                fsize /= 1024.0;
                LogOutput.log("Wrote {0:0.0} kilobytes.\n".FormatWith(fsize));
            }
        }

    };

    public class GCodePathConfig
    {
        public int speed;
        public int lineWidth;
        public string name;
        public bool spiralize;

        public GCodePathConfig() { }
        public GCodePathConfig(int speed, int lineWidth, string name)
        {
            this.speed = speed;
            this.lineWidth = lineWidth;
            this.name = name;
        }

        public void setData(int speed, int lineWidth, string name)
        {
            this.speed = speed;
            this.lineWidth = lineWidth;
            this.name = name;
        }
    }

    public class GCodePath
    {
        public GCodePathConfig config;
        public bool retract;
        public int extruder;
        public List<IntPoint> points = new List<IntPoint>();
        public bool done;//Path is finished, no more moves should be added, and a new path should be started instead of any appending done to this one.
    }

    public class GCodePlanner
    {
        GCodeExport gcode = new GCodeExport();

        IntPoint lastPosition;
        List<GCodePath> paths = new List<GCodePath>();
        Comb comb;

        GCodePathConfig travelConfig = new GCodePathConfig();
        int extrudeSpeedFactor;
        int travelSpeedFactor;
        int currentExtruder;
        int retractionMinimalDistance;
        bool forceRetraction;
        bool alwaysRetract;
        double extraTime;
        double totalPrintTime;

        public GCodePlanner(GCodeExport gcode, int travelSpeed, int retractionMinimalDistance)
        {
            this.gcode = gcode;
            travelConfig = new GCodePathConfig(travelSpeed, 0, "travel");

            lastPosition = gcode.getPositionXY();
            comb = null;
            extrudeSpeedFactor = 100;
            travelSpeedFactor = 100;
            extraTime = 0.0;
            totalPrintTime = 0.0;
            forceRetraction = false;
            alwaysRetract = false;
            currentExtruder = gcode.getExtruderNr();
            this.retractionMinimalDistance = retractionMinimalDistance;
        }

        GCodePath getLatestPathWithConfig(GCodePathConfig config)
        {
            if (paths.Count > 0 && paths[paths.Count - 1].config == config && !paths[paths.Count - 1].done)
                return paths[paths.Count - 1];
            paths.Add(new GCodePath());
            GCodePath ret = paths[paths.Count - 1];
            ret.retract = false;
            ret.config = config;
            ret.extruder = currentExtruder;
            ret.done = false;
            return ret;
        }

        void forceNewPathStart()
        {
            if (paths.Count > 0)
                paths[paths.Count - 1].done = true;
        }

        public bool setExtruder(int extruder)
        {
            if (extruder == currentExtruder)
                return false;
            currentExtruder = extruder;
            return true;
        }

        public int getExtruder()
        {
            return currentExtruder;
        }

        public void setCombBoundary(Polygons polygons)
        {
            if (polygons != null)
                comb = new Comb(polygons);
            else
                comb = null;
        }

        public void setAlwaysRetract(bool alwaysRetract)
        {
            this.alwaysRetract = alwaysRetract;
        }

        public void forceRetract()
        {
            forceRetraction = true;
        }

        public void setExtrudeSpeedFactor(int speedFactor)
        {
            if (speedFactor < 1) speedFactor = 1;
            this.extrudeSpeedFactor = speedFactor;
        }

        public int getExtrudeSpeedFactor()
        {
            return this.extrudeSpeedFactor;
        }

        public void setTravelSpeedFactor(int speedFactor)
        {
            if (speedFactor < 1) speedFactor = 1;
            this.travelSpeedFactor = speedFactor;
        }

        public int getTravelSpeedFactor()
        {
            return this.travelSpeedFactor;
        }

        public void addTravel(IntPoint p)
        {
            GCodePath path = getLatestPathWithConfig(travelConfig);
            if (forceRetraction)
            {
                if (!(lastPosition - p).shorterThen(retractionMinimalDistance))
                {
                    path.retract = true;
                }
                forceRetraction = false;
            }
            else if (comb != null)
            {
                List<IntPoint> pointList = new List<IntPoint>();
                if (comb.calc(lastPosition, p, pointList))
                {
                    for (int n = 0; n < pointList.Count; n++)
                    {
                        path.points.Add(pointList[n]);
                    }
                }
                else
                {
                    if (!(lastPosition - p).shorterThen(retractionMinimalDistance))
                        path.retract = true;
                }
            }
            else if (alwaysRetract)
            {
                if (!(lastPosition - p).shorterThen(retractionMinimalDistance))
                    path.retract = true;
            }
            path.points.Add(p);
            lastPosition = p;
        }

        public void addExtrusionMove(IntPoint p, GCodePathConfig config)
        {
            getLatestPathWithConfig(config).points.Add(p);
            lastPosition = p;
        }

        public void moveInsideCombBoundary(int distance)
        {
            if (comb == null || comb.checkInside(lastPosition)) return;
            IntPoint p = lastPosition;
            if (comb.moveInside(p, distance))
            {
                //Move inside again, so we move out of tight 90deg corners
                comb.moveInside(p, distance);
                if (comb.checkInside(p))
                {
                    addTravel(p);
                    //Make sure the that any retraction happens after this move, not before it by starting a new move path.
                    forceNewPathStart();
                }
            }
        }

        public void addPolygon(Polygon polygon, int startIdx, GCodePathConfig config)
        {
            IntPoint p0 = polygon[startIdx];
            addTravel(p0);
            for (int i = 1; i < polygon.Count; i++)
            {
                IntPoint p1 = polygon[(startIdx + i) % polygon.Count];
                addExtrusionMove(p1, config);
                p0 = p1;
            }
            if (polygon.Count > 2)
                addExtrusionMove(polygon[startIdx], config);
        }

        public void addPolygonsByOptimizer(Polygons polygons, GCodePathConfig config)
        {
            PathOrderOptimizer orderOptimizer = new PathOrderOptimizer(lastPosition);
            for (int i = 0; i < polygons.Count; i++)
            {
                orderOptimizer.addPolygon(polygons[i]);
            }

            orderOptimizer.optimize();
            
            for (int i = 0; i < orderOptimizer.polyOrder.Count; i++)
            {
                int nr = orderOptimizer.polyOrder[i];
                addPolygon(polygons[nr], orderOptimizer.polyStart[nr], config);
            }
        }

        public void forceMinimalLayerTime(double minTime, int minimalSpeed)
        {
            IntPoint p0 = gcode.getPositionXY();
            double travelTime = 0.0;
            double extrudeTime = 0.0;
            for (int n = 0; n < paths.Count; n++)
            {
                GCodePath path = paths[n];
                for (int i = 0; i < path.points.Count; i++)
                {
                    double thisTime = (p0 - path.points[i]).vSizeMM() / (double)(path.config.speed);
                    if (path.config.lineWidth != 0)
                        extrudeTime += thisTime;
                    else
                        travelTime += thisTime;
                    p0 = path.points[i];
                }
            }
            double totalTime = extrudeTime + travelTime;
            if (totalTime < minTime && extrudeTime > 0.0)
            {
                double minExtrudeTime = minTime - travelTime;
                if (minExtrudeTime < 1)
                    minExtrudeTime = 1;
                double factor = extrudeTime / minExtrudeTime;
                for (int n = 0; n < paths.Count; n++)
                {
                    GCodePath path = paths[n];
                    if (path.config.lineWidth == 0)
                        continue;
                    int speed = (int)(path.config.speed * factor);
                    if (speed < minimalSpeed)
                        factor = (double)(minimalSpeed) / (double)(path.config.speed);
                }

                //Only slow down with the minimal time if that will be slower then a factor already set. First layer slowdown also sets the speed factor.
                if (factor * 100 < getExtrudeSpeedFactor())
                    setExtrudeSpeedFactor((int)(factor * 100));
                else
                    factor = getExtrudeSpeedFactor() / 100.0;

                if (minTime - (extrudeTime / factor) - travelTime > 0.1)
                {
                    //TODO: Use up this extra time (circle around the print?)
                    this.extraTime = minTime - (extrudeTime / factor) - travelTime;
                }
                this.totalPrintTime = (extrudeTime / factor) + travelTime;
            }
            else
            {
                this.totalPrintTime = totalTime;
            }
        }

        public void writeGCode(bool liftHeadIfNeeded, int layerThickness)
        {
            GCodePathConfig lastConfig = null;
            int extruder = gcode.getExtruderNr();

            for (int n = 0; n < paths.Count; n++)
            {
                GCodePath path = paths[n];
                if (extruder != path.extruder)
                {
                    extruder = path.extruder;
                    gcode.switchExtruder(extruder);
                }
                else if (path.retract)
                {
                    gcode.addRetraction();
                }
                if (path.config != travelConfig && lastConfig != path.config)
                {
                    gcode.addComment("TYPE:{0}".FormatWith(path.config.name));
                    lastConfig = path.config;
                }
                int speed = path.config.speed;

                if (path.config.lineWidth != 0)// Only apply the extrudeSpeedFactor to extrusion moves
                    speed = speed * extrudeSpeedFactor / 100;
                else
                    speed = speed * travelSpeedFactor / 100;

                if (path.points.Count == 1 && path.config != travelConfig && (gcode.getPositionXY() - path.points[0]).shorterThen(path.config.lineWidth * 2))
                {
                    //Check for lots of small moves and combine them into one large line
                    IntPoint p0 = path.points[0];
                    int i = n + 1;
                    while (i < paths.Count && paths[i].points.Count == 1 && (p0 - paths[i].points[0]).shorterThen(path.config.lineWidth * 2))
                    {
                        p0 = paths[i].points[0];
                        i++;
                    }
                    if (paths[i - 1].config == travelConfig)
                    {
                        i--;
                    }

                    if (i > n + 2)
                    {
                        p0 = gcode.getPositionXY();
                        for (int x = n; x < i - 1; x += 2)
                        {
                            long oldLen = (p0 - paths[x].points[0]).vSize();
                            IntPoint newPoint = (paths[x].points[0] + paths[x + 1].points[0]) / 2;
                            long newLen = (gcode.getPositionXY() - newPoint).vSize();
                            if (newLen > 0)
                            {
                                gcode.addMove(newPoint, speed, (int)(path.config.lineWidth * oldLen / newLen));
                            }

                            p0 = paths[x + 1].points[0];
                        }
                        gcode.addMove(paths[i - 1].points[0], speed, path.config.lineWidth);
                        n = i - 1;
                        continue;
                    }
                }

                bool spiralize = path.config.spiralize;
                if (spiralize)
                {
                    //Check if we are the last spiralize path in the list, if not, do not spiralize.
                    for (int m = n + 1; m < paths.Count; m++)
                    {
                        if (paths[m].config.spiralize)
                            spiralize = false;
                    }
                }
                if (spiralize)
                {
                    //If we need to spiralize then raise the head slowly by 1 layer as this path progresses.
                    double totalLength = 0;
                    int z = gcode.getPositionZ();
                    IntPoint p0 = gcode.getPositionXY();
                    for (int i = 0; i < path.points.Count; i++)
                    {
                        IntPoint p1 = path.points[i];
                        totalLength += (p0 - p1).vSizeMM();
                        p0 = p1;
                    }

                    double length = 0.0;
                    p0 = gcode.getPositionXY();
                    for (int i = 0; i < path.points.Count; i++)
                    {
                        IntPoint p1 = path.points[i];
                        length += (p0 - p1).vSizeMM();
                        p0 = p1;
                        gcode.setZ((int)(z + layerThickness * length / totalLength));
                        gcode.addMove(path.points[i], speed, path.config.lineWidth);
                    }
                }
                else
                {
                    for (int i = 0; i < path.points.Count; i++)
                    {
                        gcode.addMove(path.points[i], speed, path.config.lineWidth);
                    }
                }
            }

            gcode.updateTotalPrintTime();
            if (liftHeadIfNeeded && extraTime > 0.0)
            {
                gcode.addComment("Small layer, adding delay of {0}".FormatWith(extraTime));
                gcode.addRetraction();
                gcode.setZ(gcode.getPositionZ() + 3000);
                gcode.addMove(gcode.getPositionXY(), travelConfig.speed, 0);
                gcode.addMove(gcode.getPositionXY() - new IntPoint(-20000, 0), travelConfig.speed, 0);
                gcode.addDelay(extraTime);
            }
        }
    }
}