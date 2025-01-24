using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Linq;
using System.Numerics;

namespace Ryujinx.Ava.UI.Controls
{
    public class Motion : Control
    {
        private double _XRotation = 0;
        private double _YRotation = 0;
        private double _ZRotation = 0;
        private bool _isRight;
        double length = 11;
        double width = 4;
        double height = 27;

        public void UpdateRotationFromMotionData(Vector3 accelerometerData, Vector3 gyroData, bool isRight = false)
        {
            _XRotation = Math.Atan2(-accelerometerData.Y, -accelerometerData.Z) * 180 / Math.PI;

            //TODO: issue
            _YRotation = Math.Atan2(-accelerometerData.X, -accelerometerData.Y) * 180 / Math.PI;

            _ZRotation = Math.Atan2(accelerometerData.X, -accelerometerData.Z) * 180 / Math.PI;

            _isRight = isRight;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            double size = new[] { length, width, height }.Max();
            var centerX = _isRight ? -size : size;

            DrawCube(context, centerX, size, _XRotation, 0, 0);
            if (_isRight)
            {
                DrawCube(context, centerX, 4 * size, -_YRotation - 90, 180, 90);
            }
            else
            {
                DrawCube(context, centerX, 4 * size, _YRotation + 90, 0, 90);
            }
            DrawCube(context, centerX, 7 * size, 0, 0, _ZRotation);
        }

        private void DrawCube(DrawingContext context, double centerX, double centerY, double xRotation,
            double yRotation, double zRotation)
        {
            Point3D[] cubeVertices =
            [
                new(-length, -height, -width), new(length, -height, -width), new(length, height, -width),
                new(-length, height, -width), new(-length, -height, width), new(length, -height, width),
                new(length, height, width), new(-length, height, width)
            ];

            Point[] projectedPoints = new Point[cubeVertices.Length];

            Point3D[] rotatedVertices = new Point3D[cubeVertices.Length];
            for (int i = 0; i < cubeVertices.Length; i++)
            {
                Point3D rotatedPoint = RotatePoint(cubeVertices[i], xRotation, yRotation, zRotation);
                rotatedVertices[i] = rotatedPoint;

                double projectedX = centerX + rotatedPoint.X / (1 - rotatedPoint.Z / 200);
                double projectedY = centerY + rotatedPoint.Y / (1 - rotatedPoint.Z / 200);

                projectedPoints[i] = new Point(projectedX, projectedY);
            }

            int[][] cubeFaces =
            [
                [0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [2, 3, 7, 6],
                [0, 3, 7, 4], [1, 2, 6, 5]
            ];

            IImmutableSolidColorBrush[] faceColors = _isRight
                ?
                [
                    Brushes.DimGray, Brushes.IndianRed, Brushes.DimGray, Brushes.IndianRed, Brushes.DimGray,
                    Brushes.IndianRed
                ]
                :
                [
                    Brushes.DimGray, Brushes.SkyBlue, Brushes.DimGray, Brushes.SkyBlue, Brushes.SkyBlue, Brushes.DimGray
                ];


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
                int[] face = faceData.Face;
                IImmutableSolidColorBrush color = faceData.Color;

                PathGeometry faceGeometry = new();
                PathFigure faceFigure = new()
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

        private Point3D RotatePoint(Point3D point, double xRotation, double yRotation, double zRotation)
        {
            double radX = xRotation * Math.PI / 180;
            double radY = yRotation * Math.PI / 180;
            double radZ = zRotation * Math.PI / 180;

            double cosX = Math.Cos(radX), sinX = Math.Sin(radX);
            double cosY = Math.Cos(radY), sinY = Math.Sin(radY);
            double cosZ = Math.Cos(radZ), sinZ = Math.Sin(radZ);

            double newX = cosY * cosZ * point.X + (cosY * sinZ * point.Y) - (sinY * point.Z);
            double newY = (sinX * sinY * cosZ - cosX * sinZ) * point.X + (sinX * sinY * sinZ + cosX * cosZ) * point.Y +
                          sinX * cosY * point.Z;
            double newZ = (cosX * sinY * cosZ + sinX * sinZ) * point.X + (cosX * sinY * sinZ - sinX * cosZ) * point.Y +
                          cosX * cosY * point.Z;

            return new Point3D(newX, newY, newZ);
        }

        private record Point3D(double X, double Y, double Z);
    }
}
