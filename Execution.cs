using Rhino;
using Rhino.Geometry;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Input;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.Design;
using System;
using Rhino.Input.Custom;
using Rhino.UI;

namespace YourNamespace
{
    public class NestizzatoreNuevoCommand : Command
    {
        public override string EnglishName => "ExecBn";

        // PEZZO SELEZIONATO
        public class Data
        {
            public Surface surface;
            public Curve[] border;
            public List<Curve> intersectionCurves;
            public string name;
        }

        //PEZZI DA SEZIONARE
        public class ParteIntersezione
        {
            public Brep brep;
            public RhinoObject rhinoObject;
            public string name;
        }

        protected override Result RunCommand(RhinoDoc doc, RunMode mode)
        {
            // Prompt the user to select the Breps
            ObjRef[] partsbrep;
            using (GetObject go = new GetObject())
            {
                go.SetCommandPrompt("Select parts");
                go.GeometryFilter = ObjectType.Brep;
                go.GroupSelect = true;
                go.SubObjectSelect = false;
                go.GetMultiple(1, 0);
                if (go.CommandResult() != Result.Success)
                    return go.CommandResult();
                partsbrep = go.Objects();
            }

            // Iterate over the selected Breps
            int rowIndex = 0;
            foreach (ObjRef partbrep in partsbrep)
            {
                List<BrepFace> faces = GetLargestFaces(partbrep.Brep()).ToList();
                Brep face0 = faces.First().DuplicateFace(true);
                Brep face1 = faces[1].DuplicateFace(true);
                List<Curve> set1 = ScaleAndOrientSurface(partbrep, face0);
                List<Curve> set2 = ScaleAndOrientSurface(partbrep, face1);
                // Add the optimal set of curves to the document
                IEnumerable<Curve> optimalSet = set1.Count() > set2.Count() ? set1 : set2;

                List<Curve> optimalSetList = optimalSet.ToList();
                AddTextLabels(doc, optimalSetList);
                foreach (var traccia in optimalSet)
                {
                    // Offset the curves based on the rowIndex to place them in a row
                    Transform translation = Transform.Translation(rowIndex * 4000, 0, 0);
                    traccia.Transform(translation);

                    doc.Objects.AddCurve(traccia);
                }

            }

            doc.Views.Redraw();
            return Result.Success;
        }

        public List<Curve> ScaleAndOrientSurface(ObjRef partbrep, Brep face0)
        {
            List<Curve> transcurve = new List<Curve>();
            List<Curve> intersections = new List<Curve>();

            // Get the surface to be scaled
            Surface parttobescaled = face0.Surfaces[0];

            // Get the centroid of the Brep
            Point3d centroid = AreaMassProperties.Compute(face0).Centroid;
            RhinoDoc doc = RhinoDoc.ActiveDoc;
            intersections = CalculateIntersections(doc, face0);

            // Orient the Brep
            // Get the closest point on the Brep to the centroid
            double ci;
            double t;
            face0.Surfaces[0].ClosestPoint(centroid, out ci, out t);
            // Get the frame of the Brep at the closest point
            Plane frame;
            face0.Surfaces[0].FrameAt(ci, t, out frame);
            // Create a transformation to orient the Brep in the WorldXYPlane
            Transform xform = Transform.PlaneToPlane(frame, Plane.WorldXY);
            // Apply the transformation to the Brep
            face0.Transform(xform);
            // Add the transformed curves to the document
            foreach (Curve curve in intersections)
            {
                curve.Transform(xform);
                transcurve.Add(curve);
            }
            //List<Curve> insideCurves = KeepCurvesInsideLongestCurve(transcurve, face0);
            //List<Curve> filteredCurves = FilterOverlappingCurves(insideCurves, face0);
            doc.Objects.Add(face0);

            return intersections;
        }

        public List<BrepFace> GetLargestFaces(Brep brep)
        {
            List<BrepFace> faces = new List<BrepFace>(brep.Faces.Count);

            // Find the largest face
            var largestFace = brep.Faces.Aggregate((f1, f2) => f1.ToBrep().GetArea() > f2.ToBrep().GetArea() ? f1 : f2);
            faces.Add(largestFace);

            // Remove the largest face and find the second largest
            BrepFace secondLargestFace = brep.Faces.OrderByDescending(f => f.ToBrep().GetArea()).ElementAt(1);
            faces.Add(secondLargestFace);

            return faces;
        }






        /*
         *  finding the outer boundary curves and inner boundary curves separately. We can accomplish this by implementing a graph-based solution.

Create a graph where each curve is represented as a node, and two nodes are connected if their corresponding curves intersect.

Find the connected components in the graph.

For each connected component, calculate the total length of the curves in that component.

The component with the maximum total length will be considered as the outer boundary. The other components will be inner boundaries.
         */
        public Dictionary<Curve, List<Curve>> CreateIntersectionGraph(List<Curve> curves)
        {
            var graph = new Dictionary<Curve, List<Curve>>();

            foreach (var curveA in curves)
            {
                graph[curveA] = new List<Curve>();

                foreach (var curveB in curves)
                {
                    if (curveA == curveB) continue;

                    var intersectionEvents = Rhino.Geometry.Intersect.Intersection.CurveCurve(curveA, curveB, 0.01, 0.01);
                    if (intersectionEvents.Count > 0)
                    {
                        graph[curveA].Add(curveB);
                    }
                }
            }

            return graph;
        }

        public List<List<Curve>> FindConnectedComponents(Dictionary<Curve, List<Curve>> graph)
        {
            var components = new List<List<Curve>>();
            var visited = new HashSet<Curve>();

            foreach (var node in graph.Keys)
            {
                if (!visited.Contains(node))
                {
                    var component = new List<Curve>();
                    DFS(node, graph, visited, component);
                    components.Add(component);
                }
            }

            return components;
        }

        public void DFS(Curve node, Dictionary<Curve, List<Curve>> graph, HashSet<Curve> visited, List<Curve> component)
        {
            visited.Add(node);
            component.Add(node);

            foreach (var neighbor in graph[node])
            {
                if (!visited.Contains(neighbor))
                {
                    DFS(neighbor, graph, visited, component);
                }
            }
        }

        public List<List<Curve>> CalculateBoundaryComponents(List<List<Curve>> connectedComponents)
        {
            var boundaryComponents = new List<List<Curve>>();
            double maxTotalLength = double.MinValue;

            foreach (var component in connectedComponents)
            {
                double totalLength = component.Sum(curve => curve.GetLength());

                if (totalLength > maxTotalLength)
                {
                    maxTotalLength = totalLength;
                    boundaryComponents.Insert(0, component); // Add outer boundary at the beginning
                }
                else
                {
                    boundaryComponents.Add(component); // Add inner boundaries at the end
                }
            }

            return boundaryComponents;
        }

        [Obsolete]
        public List<Curve> CalculateIntersections(RhinoDoc doc, Brep brep)
        {
            List<Curve> curvesret = new List<Curve>();
            foreach (RhinoObject rhinoObject in doc.Objects)
            {
                if (rhinoObject.ObjectType == ObjectType.Brep)
                {
                    Brep brepsurface2 = rhinoObject.Geometry as Brep;

                    if (brep == brepsurface2)
                        continue;

                    Curve[] curves;
                    Point3d[] points;
                    Rhino.Geometry.Intersect.Intersection.BrepBrep(brep, brepsurface2, 1, out curves, out points);

                    foreach (var curve in curves)
                    {
                        ObjectAttributes attributes = new ObjectAttributes
                        {
                            ObjectColor = System.Drawing.Color.Red,
                            ColorSource = ObjectColorSource.ColorFromObject
                        };
                        curvesret.Add(curve);
                        doc.Objects.AddCurve(curve, attributes);
                    }
                }
            }

            return curvesret;
        }


        public void AddTextLabels(RhinoDoc doc, List<Curve> curves)
        {
            double arrowSize = 10; // Adjust the arrow size as needed

            foreach (Curve curve in curves)
            {
                Vector3d tangent = curve.TangentAtStart;
                List<Tuple<string, Vector3d>> labelTexts = new List<Tuple<string, Vector3d>>();

                if (Math.Abs(tangent.X) > Math.Abs(tangent.Y) && Math.Abs(tangent.X) > Math.Abs(tangent.Z))
                {
                    labelTexts.Add(new Tuple<string, Vector3d>("UP", Vector3d.ZAxis));
                    labelTexts.Add(new Tuple<string, Vector3d>("PS", -Vector3d.YAxis));
                }
                else if (Math.Abs(tangent.Y) > Math.Abs(tangent.X) && Math.Abs(tangent.Y) > Math.Abs(tangent.Z))
                {
                    labelTexts.Add(new Tuple<string, Vector3d>("UP", Vector3d.ZAxis));
                    labelTexts.Add(new Tuple<string, Vector3d>("fwd", Vector3d.XAxis));
                }
                else if (Math.Abs(tangent.Z) > Math.Abs(tangent.X) && Math.Abs(tangent.Z) > Math.Abs(tangent.Y))
                {
                    labelTexts.Add(new Tuple<string, Vector3d>("fwd", Vector3d.XAxis));
                    labelTexts.Add(new Tuple<string, Vector3d>("PS", -Vector3d.YAxis));
                }

                Point3d labelLocation = curve.PointAtNormalizedLength(0.5);

                foreach (var labelText in labelTexts)
                {
                    // Add the text
                    TextEntity text = new TextEntity
                    {
                        Plane = Plane.WorldXY,
                        Justification = TextJustification.MiddleCenter,
                        Text = labelText.Item1,
                        TextHeight = 10, // Adjust the text height as needed
                    };

                    // Add the arrow polyline
                    Point3d arrowStart = labelLocation;
                    Point3d arrowEnd = arrowStart + labelText.Item2 * arrowSize;
                    Point3d arrowHead1 = arrowEnd - (0.2 * labelText.Item2 * arrowSize) + (0.1 * arrowSize * Vector3d.YAxis);
                    Point3d arrowHead2 = arrowEnd - (0.2 * labelText.Item2 * arrowSize) - (0.1 * arrowSize * Vector3d.YAxis);
                    Polyline arrow = new Polyline();
                    arrow.Add(arrowStart);
                    arrow.Add(arrowEnd);
                    arrow.Add(arrowHead1);
                    arrow.Add(arrowEnd);
                    arrow.Add(arrowHead2);

                    // Add the arrow polyline to the document
                    doc.Objects.AddPolyline(arrow);
                }
            }
        }

        public List<Curve> SelectLongestCurves(List<Curve> curves)
        {
            Dictionary<string, Curve> longestCurves = new Dictionary<string, Curve>();

            foreach (var curve in curves)
            {
                string originatingBrepName = curve.UserDictionary.GetString("OriginatingBrepName");
                if (longestCurves.ContainsKey(originatingBrepName))
                {
                    if (curve.GetLength() > longestCurves[originatingBrepName].GetLength())
                    {
                        longestCurves[originatingBrepName] = curve;
                    }
                }
                else
                {
                    longestCurves[originatingBrepName] = curve;
                }
            }

            return longestCurves.Values.ToList();
        }


        public Curve CreateThicknessArrow(Curve longestCurve, Brep intersectingBrep, double arrowLength)
        {
            // Find the midpoint and tangent of the longest curve
            double curveLength = longestCurve.GetLength();
            double curveMidParameter;
            longestCurve.LengthParameter(curveLength / 2, out curveMidParameter);
            Point3d midpoint = longestCurve.PointAt(curveMidParameter);
            Vector3d curveTangent = longestCurve.TangentAt(curveMidParameter);

            // Find the normal by intersecting a plane with the intersectingBrep
            Plane curvePlane = new Plane(midpoint, curveTangent);
            Curve[] intersectionCurves;
            Point3d[] intersectionPoints;
            Rhino.Geometry.Intersect.Intersection.BrepPlane(intersectingBrep, curvePlane, 1, out intersectionCurves, out intersectionPoints);

            // Get the normal from one of the intersection curves
            Curve intersectionCurve = intersectionCurves[0];
            Vector3d curveNormal = intersectionCurve.TangentAtStart;
            curveNormal.Unitize();

            // Ensure the normal points in the direction of the thickness
            Point3d testPoint = midpoint + curveNormal;
            if (intersectingBrep.IsPointInside(testPoint, 1, false))
            {
                curveNormal = -curveNormal;
            }

            double dotProduct = Vector3d.Multiply(curveNormal, curvePlane.Normal);
            Vector3d projectedNormal = curveNormal - dotProduct * curvePlane.Normal;
            projectedNormal.Unitize();
            projectedNormal *= arrowLength;

            // Create an arrow polyline
            Point3d arrowStart = midpoint;
            Point3d arrowEnd = arrowStart + projectedNormal;
            Point3d arrowHead1 = arrowEnd - (0.2 * projectedNormal) + (0.1 * arrowLength * curvePlane.YAxis);
            Point3d arrowHead2 = arrowEnd - (0.2 * projectedNormal) - (0.1 * arrowLength * curvePlane.YAxis);

            Polyline arrow = new Polyline();
            arrow.Add(arrowStart);
            arrow.Add(arrowEnd);
            arrow.Add(arrowHead1);
            arrow.Add(arrowEnd);
            arrow.Add(arrowHead2);

            // Return the arrow polyline as a curve
            return arrow.ToNurbsCurve();
        }
        public List<Curve> FilterOverlappingCurves(List<Curve> curves, Brep borderBrep)
        {
            List<Curve> filteredCurves = new List<Curve>();

            foreach (Curve curve in curves)
            {
                // Find intersections between the curve and the border curve
                Curve[] borderCurves = borderBrep.DuplicateEdgeCurves();
                Curve borderCurve = Curve.JoinCurves(borderCurves)[0];

                var curveIntersections = Rhino.Geometry.Intersect.Intersection.CurveCurve(curve, borderCurve, 1, 1);
                List<Point3d> intersectionPointsList = new List<Point3d>();
                foreach (var intersectionEvent in curveIntersections)
                {
                    if (intersectionEvent.IsPoint)
                    {
                        intersectionPointsList.Add(intersectionEvent.PointA);
                    }
                }
                Point3d[] intersectionPoints = intersectionPointsList.ToArray();

                // If there are no intersections, the curve doesn't overlap the border curve
                if (intersectionPoints.Length == 0)
                {
                    filteredCurves.Add(curve);
                    continue;
                }

                List<double> intersectionParameters = new List<double>();
                foreach (Point3d intersectionPoint in intersectionPoints)
                {
                    curve.ClosestPoint(intersectionPoint, out double t);
                    intersectionParameters.Add(t);
                }
                intersectionParameters.Sort();

                // Trim the curve segments that are outside the border curve
                List<Curve> curveSegments = new List<Curve>();
                for (int i = 0; i < intersectionParameters.Count - 1; i++)
                {
                    double midParam = (intersectionParameters[i] + intersectionParameters[i + 1]) / 2;
                    Point3d midPoint = curve.PointAt(midParam);

                    if (!borderBrep.IsPointInside(midPoint, 1, false))
                    {
                        curveSegments.Add(curve.Trim(intersectionParameters[i], intersectionParameters[i + 1]));
                    }
                }

                // If the curve has been trimmed, join the segments and add them to the filteredCurves list
                if (curveSegments.Count > 0)
                {
                    Curve[] joinedCurves = Curve.JoinCurves(curveSegments);
                    filteredCurves.AddRange(joinedCurves);
                }
                else
                {
                    // If the curve hasn't been trimmed, add the original curve to the filteredCurves list
                    filteredCurves.Add(curve);
                }
            }

            return filteredCurves;
        }
    }
}

