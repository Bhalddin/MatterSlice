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

    public class PathOrderOptimizer
    {
        public IntPoint startPoint;
        public List<Polygon> polygons = new List<Polygon>();
        public List<int> polyStart = new List<int>();
        public List<int> polyOrder = new List<int>();

        public PathOrderOptimizer(IntPoint startPoint)
        {
            this.startPoint = startPoint;
        }

        public void addPolygon(Polygon polygon)
        {
            this.polygons.Add(polygon);
        }

        public void addPolygons(Polygons polygons)
        {
            for (int i = 0; i < polygons.Count; i++)
                this.polygons.Add(polygons[i]);
        }

        public void optimize()
        {
            List<bool> picked = new List<bool>();
            for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
            {
                int bestPoint = -1;
                float closestDist = float.MaxValue;
                Polygon poly = polygons[polygonIndex];
                for (int pointIndex = 0; pointIndex < poly.Count; pointIndex++)
                {
                    float dist = (poly[pointIndex] - startPoint).vSize2f();
                    if (dist < closestDist)
                    {
                        bestPoint = pointIndex;
                        closestDist = dist;
                    }
                }
                polyStart.Add(bestPoint);
                picked.Add(false);
            }

            IntPoint p0 = startPoint;
            for (int n = 0; n < polygons.Count; n++)
            {
                int best = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < polygons.Count; i++)
                {
                    if (picked[i] || polygons[i].Count < 1)
                        continue;
                    if (polygons[i].Count == 2)
                    {
                        float dist = (polygons[i][0] - p0).vSize2f();
                        if (dist < bestDist)
                        {
                            best = i;
                            bestDist = dist;
                            polyStart[i] = 0;
                        }
                        dist = (polygons[i][1] - p0).vSize2f();
                        if (dist < bestDist)
                        {
                            best = i;
                            bestDist = dist;
                            polyStart[i] = 1;
                        }
                    }
                    else
                    {
                        float dist = (polygons[i][polyStart[i]] - p0).vSize2f();
                        if (dist < bestDist)
                        {
                            best = i;
                            bestDist = dist;
                        }
                    }
                }
                if (best > -1)
                {
                    if (polygons[best].Count == 2)
                    {
                        p0 = polygons[best][(polyStart[best] + 1) % 2];
                    }
                    else
                    {
                        p0 = polygons[best][polyStart[best]];
                    }
                    picked[best] = true;
                    polyOrder.Add(best);
                }
            }

            p0 = startPoint;
            for (int n = 0; n < polyOrder.Count; n++)
            {
                int nr = polyOrder[n];
                int best = -1;
                float bestDist = float.MaxValue;
                for (int i = 0; i < polygons[nr].Count; i++)
                {
                    float dist = (polygons[nr][i] - p0).vSize2f();
                    if (dist < bestDist)
                    {
                        best = i;
                        bestDist = dist;
                    }
                }
                polyStart[nr] = best;
                if (polygons[nr].Count <= 2)
                {
                    p0 = polygons[nr][(best + 1) % 2];
                }
                else
                {
                    p0 = polygons[nr][best];
                }
            }
        }
    }
}