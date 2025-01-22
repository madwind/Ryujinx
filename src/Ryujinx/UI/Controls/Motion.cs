using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Linq;
using System.Numerics;
using Vector = Avalonia.Vector;

namespace Ryujinx.Ava.UI.ViewModels
{
    public class Motion : Control
    {
        public double XRotation { get; set; }
        public double YRotation { get; set; }

        private double controllerPitch = 0;
        private double controllerYaw = 0;

        public void UpdateRotationFromMotionData(Vector3 accelerometerData, Vector3 gyroscopeData)
        {
            controllerPitch = Math.Atan2(accelerometerData.Y, -accelerometerData.Z) * (180 / Math.PI);
            controllerYaw = Math.Atan2(accelerometerData.X, accelerometerData.Y) * (180 / Math.PI);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            XRotation = controllerPitch;
            YRotation = controllerYaw;
            var width = Bounds.Width;
            var height = Bounds.Height;
            DrawCube(context, width, height);
        }

        private void DrawCube(DrawingContext context, double canvasWidth, double canvasHeight)
        {
            Point3D[] cubeVertices = new Point3D[]
            {
                new Point3D(-17, -48, -8), new Point3D(17, -48, -8), new Point3D(17, 48, -8),
                new Point3D(-17, 48, -8), new Point3D(-17, -48, 8), new Point3D(17, -48, 8), new Point3D(17, 48, 8),
                new Point3D(-17, 48, 8)
            };

            double centerX = canvasWidth / 2;
            double centerY = canvasHeight / 2;

            Point[] projectedPoints = new Point[cubeVertices.Length];

            Point3D[] rotatedVertices = new Point3D[cubeVertices.Length];
            for (int i = 0; i < cubeVertices.Length; i++)
            {
                Point3D rotatedPoint = RotatePoint(cubeVertices[i], XRotation, YRotation);
                rotatedVertices[i] = rotatedPoint;

                double projectedX = centerX + rotatedPoint.X / (1 - rotatedPoint.Z / 200);
                double projectedY = centerY + rotatedPoint.Y / (1 - rotatedPoint.Z / 200);

                projectedPoints[i] = new Point(projectedX, projectedY);
            }

            int[][] cubeFaces = new int[][]
            {
                new int[] { 0, 1, 2, 3 }, new int[] { 4, 5, 6, 7 }, new int[] { 0, 1, 5, 4 },
                new int[] { 2, 3, 7, 6 }, new int[] { 0, 3, 7, 4 }, new int[] { 1, 2, 6, 5 }
            };

            IImmutableSolidColorBrush[] faceColors = new IImmutableSolidColorBrush[]
            {
                Brushes.DimGray, Brushes.SkyBlue, Brushes.DimGray, Brushes.SkyBlue, Brushes.SkyBlue, Brushes.DimGray
            };


            var sortedFaces = cubeFaces
                .Select((face, index) => new
                {
                    Face = face,
                    Color = faceColors[index],
                    MinZ = face.Min(vertexIndex => rotatedVertices[vertexIndex].Z)
                })
                .OrderBy(faceData => faceData.MinZ)
                .ToArray();

            foreach (var faceData in sortedFaces)
            {
                var face = faceData.Face;
                var color = faceData.Color;

                PathGeometry faceGeometry = new PathGeometry();
                PathFigure faceFigure = new PathFigure
                {
                    StartPoint = projectedPoints[face[0]], IsClosed = true, IsFilled = true
                };

                for (int j = 1; j < face.Length; j++)
                {
                    faceFigure.Segments.Add(new LineSegment { Point = projectedPoints[face[j]] });
                }

                faceGeometry.Figures.Add(faceFigure);

                context.DrawGeometry(color, new Pen(Brushes.White), faceGeometry);
            }
        }

        private Point3D RotatePoint(Point3D point, double xAngle, double yAngle)
        {
            double cosX = Math.Cos(xAngle * Math.PI / 180);
            double sinX = Math.Sin(xAngle * Math.PI / 180);
            double cosY = Math.Cos(yAngle * Math.PI / 180);
            double sinY = Math.Sin(yAngle * Math.PI / 180);

            double newY = point.Y * cosX - point.Z * sinX;
            double newZ = point.Y * sinX + point.Z * cosX;

            double newX = point.X * cosY + newZ * sinY;
            newZ = -point.X * sinY + newZ * cosY;
            if (xAngle < 0)
            {
                newX = -newX;
                newZ = -newZ;
            }

            return new Point3D(newX, newY, newZ);
        }

        private record Point3D(double X, double Y, double Z);
    }
}
