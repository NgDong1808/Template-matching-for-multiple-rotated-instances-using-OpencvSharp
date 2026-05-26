using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;
namespace TemplateMatching
{
    /// <summary>
    /// Template Matching with rotation, pyramid, and NMS.
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        private Mat _sceneImage;
        private Mat _masterTemplate;

        public MainWindow()
        {
            InitializeComponent();
        }

        private record MatchResult(Point Location, double Angle, double Score, int W, int H, int OrigW, int OrigH);

        private void Log(string msg)
        {
            TbLog.Text += "\n" + msg;
            LogScroll.ScrollToBottom();
        }

        private void SetStatus(string text, string colorHex = null)
        {
            TbStatus.Text = "  " + text;
            if (colorHex != null)
                StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        }

        private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog { Filter = "Image files (*.png;*.jpg)|*.png;*.jpg|All files (*.*)|*.*" };
            if (ofd.ShowDialog() == true)
            {
                _sceneImage = Cv2.ImRead(ofd.FileName, ImreadModes.Grayscale);
                MainImage.Source = _sceneImage.ToBitmapSource();
                Log("> Scene loaded: " + ofd.FileName);
                SetStatus("Scene loaded", "#4CAF50");
                BtnRun.IsEnabled = (_masterTemplate != null);
            }
        }

        private void BtnLoadMaster_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog { Filter = "Image files (*.png;*.jpg)|*.png;*.jpg|All files (*.*)|*.*" };
            if (ofd.ShowDialog() == true)
            {
                _masterTemplate = Cv2.ImRead(ofd.FileName, ImreadModes.Grayscale);
                MasterPreview.Source = _masterTemplate.ToBitmapSource();
                Log("> Master loaded: " + ofd.FileName);
                SetStatus("Master loaded", "#4CAF50");
                BtnRun.IsEnabled = (_sceneImage != null);
            }
        }

        /// <summary>Builds an image pyramid.</summary>
        private static Mat[] GenPyramidImage(Mat img, int pyrLevels)
        {
            var imgPyr = new Mat[pyrLevels];
            imgPyr[0] = img;
            for (int lv = 1; lv < pyrLevels; lv++)
            {
                double s = 1.0 / (1 << lv);
                imgPyr[lv] = new Mat();
                Cv2.Resize(img, imgPyr[lv], new OpenCvSharp.Size(), s, s, InterpolationFlags.Linear);
            }
            return imgPyr;
        }

        /// <summary>Generates a rotated template and its mask.</summary>
        private static (Mat tmpl, Mat mask) GenRotatedTemplate(Mat inputImage, double angle)
        {
            int diagonal = (int)Math.Ceiling(Math.Sqrt(inputImage.Cols * inputImage.Cols + inputImage.Rows * inputImage.Rows));
            var finalSize = new OpenCvSharp.Size(diagonal, diagonal);
            Point2f center = new(inputImage.Cols / 2f, inputImage.Rows / 2f);
            using var M = Cv2.GetRotationMatrix2D(center, angle, 1.0);
            M.At<double>(0, 2) += (diagonal - inputImage.Cols) / 2.0;
            M.At<double>(1, 2) += (diagonal - inputImage.Rows) / 2.0;
            Mat tmpl = new Mat();
            Cv2.WarpAffine(inputImage, tmpl, M, finalSize, InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));
            using var srcMask = new Mat(inputImage.Size(), MatType.CV_8UC1, Scalar.All(255));
            Mat mask = new Mat();
            Cv2.WarpAffine(srcMask, mask, M, finalSize, InterpolationFlags.Nearest, BorderTypes.Constant, Scalar.All(0));
            return (tmpl, mask);
        }

        /// <summary>Non‑Maximum Suppression based on IoU.</summary>
        private ConcurrentBag<MatchResult> ApplyNMS(ConcurrentBag<MatchResult> rawMatches, double overlapThreshold)
        {
            var result = new ConcurrentBag<MatchResult>();
            var sorted = rawMatches.OrderByDescending(m => m.Score).ToList();
            while (sorted.Count > 0)
            {
                var current = sorted[0];
                result.Add(current);
                sorted.RemoveAt(0);
                double area1 = current.W * current.H;
                for (int i = sorted.Count - 1; i >= 0; i--)
                {
                    var other = sorted[i];
                    int x1 = Math.Max(current.Location.X, other.Location.X);
                    int y1 = Math.Max(current.Location.Y, other.Location.Y);
                    int x2 = Math.Min(current.Location.X + current.W, other.Location.X + other.W);
                    int y2 = Math.Min(current.Location.Y + current.H, other.Location.Y + other.H);
                    int w = Math.Max(0, x2 - x1);
                    int h = Math.Max(0, y2 - y1);
                    double interArea = w * h;
                    double area2 = other.W * other.H;
                    double unionArea = area1 + area2 - interArea;
                    double iou = interArea / unionArea;
                    if (iou > overlapThreshold)
                        sorted.RemoveAt(i);
                }
            }
            return result;
        }

        /// <summary>Draws rotated bounding boxes and center points on the result image.</summary>
        private void DrawResult(Mat DisImage, Mat masterFull, ConcurrentBag<MatchResult> rawMatches, int tmpIndex)
        {
            int scale = 1 << tmpIndex;
            foreach (var c in rawMatches)
            {
                int originalX = c.OrigW * scale;
                int originalY = c.OrigH * scale;
                int centerX = originalX + (c.W * scale) / 2;
                int centerY = originalY + (c.H * scale) / 2;
                Point horizontalStart = new Point(centerX - 5, centerY);
                Point horizontalEnd = new Point(centerX + 5, centerY);
                Point verticalStart = new Point(centerX, centerY - 5);
                Point verticalEnd = new Point(centerX, centerY + 5);
                Cv2.Line(DisImage, horizontalStart, horizontalEnd, Scalar.LightGreen, 1);
                Cv2.Line(DisImage, verticalStart, verticalEnd, Scalar.LightGreen, 1);

                Point2f center = new Point2f(centerX, centerY);
                Size2f size = new Size2f(masterFull.Width, masterFull.Height);
                RotatedRect rotatedRect = new RotatedRect(center, size, (float)c.Angle);
                Point2f[] vertices = rotatedRect.Points();
                for (int i = 0; i < 4; i++)
                {
                    Point pt1 = new Point((int)vertices[i].X, (int)vertices[i].Y);
                    Point pt2 = new Point((int)vertices[(i + 1) % 4].X, (int)vertices[(i + 1) % 4].Y);
                    Cv2.Line(DisImage, pt1, pt2, Scalar.LightGreen, 2);
                }
            }
        }

        /// <summary>Performs coarse‑to‑fine template matching.</summary>
        private async void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (_masterTemplate == null)
            {
                MessageBox.Show("Please load a master image.", "Missing Master", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_sceneImage == null)
            {
                MessageBox.Show("Please load a scene image.", "Missing Scene", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TbMaxMatches.Text, out int maxMatches)) maxMatches = 10;
            if (!int.TryParse(TbPyramidLevels.Text, out int pyrLevels)) pyrLevels = 3;
            if (!double.TryParse(TbMinScore.Text, out double minScore)) minScore = 0.80;
            if (!double.TryParse(TbNMSOverlap.Text, out double nmsOvlp)) nmsOvlp = 0.30;
            if (!double.TryParse(TbAngleFrom.Text, out double angleFrom)) angleFrom = 0;
            if (!double.TryParse(TbAngleTo.Text, out double angleTo)) angleTo = 0;
            if (!double.TryParse(TbAngleStep.Text, out double angleStep)) angleStep = 5.0;
            if (angleStep <= 0) angleStep = 1.0;

            BtnRun.IsEnabled = false;
            SetStatus("Processing…", "#FF9800");
            Stopwatch sw = Stopwatch.StartNew();

            Mat imageFull = _sceneImage.Clone();
            Mat DisImage = _sceneImage.Clone();
            Cv2.CvtColor(DisImage, DisImage, ColorConversionCodes.GRAY2BGR);
            Mat masterFull = _masterTemplate.Clone();

            Mat[] MasterPyr = GenPyramidImage(masterFull, pyrLevels);
            Mat[] ImagePyr = GenPyramidImage(imageFull, pyrLevels);

            int angleCount = (int)Math.Max(1, Math.Ceiling((angleTo - angleFrom) / angleStep));
            int coarsestLv = pyrLevels - 1;
            int coarseScale = 1 << coarsestLv;
            float coarseThreshold = (float)Math.Max(0.5, minScore - 0.15);

            ConcurrentBag<MatchResult> coarseCandidates = new ConcurrentBag<MatchResult>();

            Parallel.For(0, angleCount, k =>
            {
                double angle = angleFrom + k * angleStep;
                var (tmpl, mask) = GenRotatedTemplate(MasterPyr[coarsestLv], angle);
                Mat result = new Mat();
                Cv2.MatchTemplate(ImagePyr[coarsestLv], tmpl, result, TemplateMatchModes.CCoeffNormed, mask);
                unsafe
                {
                    float* ptr = (float*)result.DataPointer;
                    for (int y = 0; y < result.Rows; y++)
                    {
                        int rowOffset = y * result.Cols;
                        for (int x = 0; x < result.Cols; x++)
                        {
                            float score = ptr[rowOffset + x];
                            if (!float.IsFinite(score)) continue;
                            if (score >= coarseThreshold)
                            {
                                int fullX = x * coarseScale;
                                int fullY = y * coarseScale;
                                coarseCandidates.Add(new MatchResult(new Point(fullX, fullY), angle, score,
                                    tmpl.Width * coarseScale, tmpl.Height * coarseScale, fullX, fullY));
                            }
                        }
                    }
                }
                result.Dispose();
                tmpl.Dispose();
                mask.Dispose();
            });

            Log($"> Coarse level {coarsestLv}: {coarseCandidates.Count} raw candidates");
            coarseCandidates = ApplyNMS(coarseCandidates, nmsOvlp);
            Log($"> After NMS coarse: {coarseCandidates.Count} candidates");

            int diagFull = (int)Math.Ceiling(Math.Sqrt(masterFull.Cols * (double)masterFull.Cols + masterFull.Rows * (double)masterFull.Rows));
            ConcurrentBag<MatchResult> rawMatches = new ConcurrentBag<MatchResult>();

            Parallel.ForEach(coarseCandidates, coarseMatch =>
            {
                int pad = coarseScale;
                int rx = Math.Max(0, coarseMatch.Location.X - pad);
                int ry = Math.Max(0, coarseMatch.Location.Y - pad);
                int rw = Math.Min(imageFull.Cols - rx, diagFull + pad * 2);
                int rh = Math.Min(imageFull.Rows - ry, diagFull + pad * 2);
                if (rw <= 0 || rh <= 0) return;

                var (fTmpl, fMask) = GenRotatedTemplate(MasterPyr[0], coarseMatch.Angle);
                if (fTmpl.Cols > rw || fTmpl.Rows > rh)
                {
                    fTmpl.Dispose(); fMask.Dispose();
                    return;
                }
                using Mat searchROI = new Mat(imageFull, new Rect(rx, ry, rw, rh));
                Mat fResult = new Mat();
                Cv2.MatchTemplate(searchROI, fTmpl, fResult, TemplateMatchModes.CCoeffNormed, fMask);
                Cv2.MinMaxLoc(fResult, out _, out double maxVal, out _, out Point maxLoc);
                if (maxVal >= minScore)
                {
                    int absX = rx + maxLoc.X;
                    int absY = ry + maxLoc.Y;
                    rawMatches.Add(new MatchResult(new Point(absX, absY), coarseMatch.Angle, maxVal,
                        fTmpl.Width, fTmpl.Height, absX, absY));
                }
                fResult.Dispose();
                fTmpl.Dispose();
                fMask.Dispose();
            });

            rawMatches = ApplyNMS(rawMatches, nmsOvlp);
            rawMatches = new ConcurrentBag<MatchResult>(rawMatches.OrderByDescending(m => m.Score).Take(maxMatches));

            DrawResult(DisImage, masterFull, rawMatches, 0);
            sw.Stop();

            foreach (var m in rawMatches.OrderByDescending(x => x.Score))
                Log($"  Score={m.Score:F3}  Angle={m.Angle:F1}°  Loc=({m.Location.X},{m.Location.Y})");
            Log($"> Result: {rawMatches.Count} match(es) — {sw.ElapsedMilliseconds} ms");

            var best = rawMatches.OrderByDescending(m => m.Score).FirstOrDefault();
            if (best != null)
            {
                TbPoint.Text = $"({best.Location.X}, {best.Location.Y})";
                TbAngle.Text = $"{best.Angle:F1}°";
                TbArea.Text = $"{best.W}x{best.H}";
                TbScore.Text = $"{best.Score:F3}";
                TbElapsed.Text = $"{sw.ElapsedMilliseconds} ms";
                SetStatus("End", "#4CAF50");
            }
            else
            {
                TbPoint.Text = "—"; TbAngle.Text = "—"; TbArea.Text = "—"; TbScore.Text = "—"; TbElapsed.Text = "—";
            }

            MainImage.Source = DisImage.ToBitmapSource();

            imageFull.Dispose();
            masterFull.Dispose();
            BtnRun.IsEnabled = true;
        }
    }
}