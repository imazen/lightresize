﻿using System;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Web;
using NUnit.Framework;

namespace LightResize.Tests
{
    [TestFixture]
    public class ResizeJobTests
    {
        [Test]
        [TestCase(default(JobOptions))]
        [TestCase(JobOptions.BufferEntireSourceStream)]
        [TestCase(JobOptions.BufferEntireSourceStream | JobOptions.LeaveSourceStreamOpen)]
        [TestCase(JobOptions.LeaveSourceStreamOpen)]
        [TestCase(JobOptions.LeaveSourceStreamOpen | JobOptions.RewindSourceStream)]
        public void Build_Succeeds_EvenWhenSourceStreamPositionNotAt0(JobOptions jobOptions)
        {
            TestDelegate action = () =>
            {
                using (var sourceStream = GetBitmapStream(100, 100))
                using (var targetStream = new MemoryStream())
                {
                    sourceStream.Seek(17, SeekOrigin.Begin);
                    new ResizeJob { Width = 50 }.Build(sourceStream, targetStream, jobOptions);
                }
            };
            Assert.DoesNotThrow(action);
        }

        [Test]
        [TestCase(JobOptions.LeaveSourceStreamOpen)]
        [TestCase(JobOptions.RewindSourceStream)]
        [TestCase(JobOptions.LeaveSourceStreamOpen | JobOptions.RewindSourceStream)]
        public void Build_LeavesSourceStreamOpen_WhenAskedTo(JobOptions jobOptions)
        {
            using (var sourceStream = GetBitmapStream(100, 100))
            using (var targetStream = new MemoryStream())
            {
                new ResizeJob { Width = 50 }.Build(sourceStream, targetStream, jobOptions);
                Assert.True(sourceStream.CanRead);
            }
        }

        [Test]
        [TestCase((JobOptions)0)]
        [TestCase(JobOptions.BufferEntireSourceStream)]
        [TestCase(JobOptions.CreateParentDirectory)]
        [TestCase(JobOptions.LeaveTargetStreamOpen)]
        [TestCase(JobOptions.PreserveTargetBitmap)]
        public void Build_ClosesSourceStream_WhenNotAskedToLeaveItOpen(JobOptions jobOptions)
        {
            using (var sourceStream = GetBitmapStream(100, 100))
            using (var targetStream = new MemoryStream())
            {
                new ResizeJob { Width = 50 }.Build(sourceStream, targetStream, jobOptions);
                Assert.False(sourceStream.CanRead);
            }
        }

        [Test]
        [TestCase(JobOptions.LeaveTargetStreamOpen)]
        public void Build_LeavesTargetStreamOpen_WhenAskedTo(JobOptions jobOptions)
        {
            using (var sourceStream = GetBitmapStream(100, 100))
            using (var targetStream = new MemoryStream())
            {
                new ResizeJob { Width = 50 }.Build(sourceStream, targetStream, jobOptions);
                Assert.True(targetStream.CanRead);
            }
        }

        [Test]
        [TestCase((JobOptions)0)]
        [TestCase(JobOptions.BufferEntireSourceStream)]
        [TestCase(JobOptions.CreateParentDirectory)]
        [TestCase(JobOptions.LeaveSourceStreamOpen)]
        [TestCase(JobOptions.PreserveTargetBitmap)]
        [TestCase(JobOptions.RewindSourceStream)]
        public void Build_ClosesTargetStream_WhenNotAskedToLeaveItOpen(JobOptions jobOptions)
        {
            using (var sourceStream = GetBitmapStream(100, 100))
            using (var targetStream = new MemoryStream())
            {
                new ResizeJob { Width = 50 }.Build(sourceStream, targetStream, jobOptions);
                Assert.False(targetStream.CanRead);
            }
        }

        [Test]
        [TestCase(JobOptions.RewindSourceStream)]
        [TestCase(JobOptions.BufferEntireSourceStream | JobOptions.RewindSourceStream)]
        public void Build_RewindsSourceStream_WhenAskedTo(JobOptions jobOptions)
        {
            using (var sourceStream = GetBitmapStream(100, 100))
            using (var targetStream = new MemoryStream())
            {
                sourceStream.Seek(1, SeekOrigin.Begin);
                var originalPosition = sourceStream.Position;
                new ResizeJob { Width = 50 }.Build(sourceStream, targetStream, jobOptions);
                Assume.That(sourceStream.CanSeek);
                Assert.AreEqual(originalPosition, sourceStream.Position);
            }
        }

        [Test]
        [TestCase((JobOptions)0)]
        [TestCase(JobOptions.BufferEntireSourceStream)]
        [TestCase(JobOptions.CreateParentDirectory)]
        [TestCase(JobOptions.LeaveSourceStreamOpen)]
        [TestCase(JobOptions.LeaveTargetStreamOpen)]
        [TestCase(JobOptions.PreserveTargetBitmap)]
        public void Build_DoesNotRewindSourceStream_WhenNotAskedTo(JobOptions jobOptions)
        {
            using (var sourceStream = GetBitmapStream(100, 100))
            using (var targetStream = new MemoryStream())
            {
                sourceStream.Seek(1, SeekOrigin.Begin);
                var originalPosition = sourceStream.Position;
                new ResizeJob { Width = 50 }.Build(sourceStream, targetStream, jobOptions | JobOptions.LeaveSourceStreamOpen);
                Assume.That(sourceStream.CanSeek);
                Assert.AreNotEqual(originalPosition, sourceStream.Position);
            }
        }

        [Test]
        public void Build_CreatesTargetDirectory_WhenAskedTo()
        {
            using (var sourceStream = GetBitmapStream(100, 100))
            using (var targetStream = new MemoryStream())
            {
                sourceStream.Seek(1, SeekOrigin.Begin);
                var originalPosition = sourceStream.Position;
                var parentDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                Assume.That(!Directory.Exists(parentDirectory));
                var targetPath = Path.Combine(parentDirectory, "test.jpg");
                try
                {
                    new ResizeJob { Width = 50 }.Build(sourceStream, targetPath, JobOptions.CreateParentDirectory);
                    Assert.True(Directory.Exists(parentDirectory));
                }
                finally
                {
                    File.Delete(targetPath);
                    Directory.Delete(parentDirectory, false);
                }
            }
        }

        [Test]
        public void Build_CannotWriteToNonExistentDirectory_WhenNotAskedToCreateIt()
        {
            TestDelegate action = () =>
            {
                using (var sourceStream = GetBitmapStream(100, 100))
                using (var targetStream = new MemoryStream())
                {
                    sourceStream.Seek(1, SeekOrigin.Begin);
                    var originalPosition = sourceStream.Position;
                    var parentDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
                    Assume.That(!Directory.Exists(parentDirectory));
                    var targetPath = Path.Combine(parentDirectory, "test.jpg");
                    new ResizeJob { Width = 50 }.Build(sourceStream, targetPath, (JobOptions)0);
                }
            };
            Assert.Throws<DirectoryNotFoundException>(action);
        }

        [Test]
        [TestCase(FitMode.Crop)]
        [TestCase(FitMode.Pad)]
        [TestCase(FitMode.Stretch)]
        public void Build_ProducesBitmapWithExactSpecifiedWidthAndHeight_GivenFitMode(FitMode mode)
        {
            using (var sourceStream = GetBitmapStream(100, 100))
            using (var targetStream = Stream.Null)
            {
                var job = new ResizeJobWithExplicitConsumer { Mode = mode, Width = 12, Height = 34 };
                job.Build(sourceStream, default(JobOptions), (output) =>
                {
                    Assert.AreEqual(output.Width, 12);
                    Assert.AreEqual(output.Height, 34);
                });
            }
        }

        [Test]
        public void Build_ProducesBitmapWithSmallerMaxAndSameAspectRatio_GivenFitModeMax()
        {
            using (var sourceStream = GetBitmapStream(100, 66))
            using (var targetStream = Stream.Null)
            {
                var job = new ResizeJobWithExplicitConsumer { Mode = FitMode.Max, Width = 12, Height = 34 };
                job.Build(sourceStream, default(JobOptions), (output) =>
                {
                    Assert.AreEqual(output.Width, 12);
                    Assert.AreEqual(output.Height, 8);
                });
            }
        }

        [Test]
        public void Build_ProducesBitmapNoLargerThanOriginal_GivenScaleModeDown()
        {
            using (var sourceStream = GetBitmapStream(100, 100))
            using (var targetStream = Stream.Null)
            {
                var job = new ResizeJobWithExplicitConsumer { Scale = ScaleMode.DownscaleOnly, Width = 200, Height = 200 };
                job.Build(sourceStream, default(JobOptions), (output) =>
                {
                    Assert.AreEqual(output.Width, 100);
                    Assert.AreEqual(output.Height, 100);
                });
            }
        }

        [Test]
        [TestCase(ScaleMode.Both)]
        [TestCase(ScaleMode.UpscaleCanvas)]
        public void Build_ProducesBitmapLargerThanOriginal_GivenScaleMode(ScaleMode scale)
        {
            using (var sourceStream = GetBitmapStream(100, 100))
            using (var targetStream = Stream.Null)
            {
                var job = new ResizeJobWithExplicitConsumer { Scale = scale, Width = 200, Height = 200 };
                job.Build(sourceStream, default(JobOptions), (output) =>
                {
                    Assert.AreEqual(output.Width, 200);
                    Assert.AreEqual(output.Height, 200);
                });
            }
        }

        [Test]
        [TestCase(50, 50, OutputFormat.Jpeg, 100)]
        [TestCase(1, 1, OutputFormat.Jpeg, 100)]
        [TestCase(50, 50, OutputFormat.Jpeg, -300)]
        [TestCase(50, 50, OutputFormat.Png)]
        public void EncodeImage(int width, int height, OutputFormat format, int? jpegQuality = null)
        {
            using (MemoryStream ms = new MemoryStream(8000))
            {
                var job = new ResizeJob(format: format, jpegQuality: jpegQuality ?? 90)
                {
                    Format = format,
                    JpegQuality = jpegQuality ?? 90,
                };
                job.Build(GetBitmapStream(width, height), ms, JobOptions.LeaveTargetStreamOpen);
            }
        }

        private Bitmap GetBitmap(int width, int height)
        {
            Bitmap b = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.DrawString("Hello!", new Font(FontFamily.GenericSansSerif, 1), new SolidBrush(Color.Beige), new PointF(0, 0));
                g.Flush();
            }

            return b;
        }

        private Stream GetBitmapStream(int width, int height)
        {
            MemoryStream ms = new MemoryStream(4096);
            using (var b = GetBitmap(width, height))
            {
                b.Save(ms, ImageFormat.Png);
            }

            ms.Seek(0, SeekOrigin.Begin);
            return ms;
        }
    }
}
