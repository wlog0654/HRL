// Original application artwork. MIT licensed with the rest of the project.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

class IconBuilder {
    static Bitmap Draw(int size) {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(size / 64f, size / 64f);
            using (var path = new GraphicsPath()) {
                path.AddArc(2, 2, 24, 24, 180, 90); path.AddArc(38, 2, 24, 24, 270, 90);
                path.AddArc(38, 38, 24, 24, 0, 90); path.AddArc(2, 38, 24, 24, 90, 90); path.CloseFigure();
                using (var brush = new SolidBrush(Color.FromArgb(24, 32, 42))) g.FillPath(brush, path);
            }
            using (var pen = new Pen(Color.FromArgb(101, 205, 225), 6)) {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawArc(pen, 15, 15, 34, 34, 205, 128);
            }
            using (var pen = new Pen(Color.FromArgb(151, 224, 203), 6)) {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                g.DrawArc(pen, 15, 15, 34, 34, 25, 128);
            }
            using (var brush = new SolidBrush(Color.FromArgb(101, 205, 225))) g.FillPolygon(brush, new[] { new PointF(52, 14), new PointF(51, 30), new PointF(37, 23) });
            using (var brush = new SolidBrush(Color.FromArgb(151, 224, 203))) g.FillPolygon(brush, new[] { new PointF(12, 50), new PointF(13, 34), new PointF(27, 41) });
        }
        return bitmap;
    }
    static int Main(string[] args) {
        string root = args[0]; Directory.CreateDirectory(root);
        int[] sizes = {16, 20, 24, 32, 40, 48, 64, 128, 256};
        var frames = new List<byte[]>();
        foreach (int size in sizes) using (var bitmap = Draw(size)) using (var stream = new MemoryStream()) {
            bitmap.Save(stream, ImageFormat.Png); frames.Add(stream.ToArray());
            if (size == 256) bitmap.Save(Path.Combine(root, "icon.png"), ImageFormat.Png);
        }
        using (var writer = new BinaryWriter(File.Create(Path.Combine(root, "AppIcon.ico")))) {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++) {
                writer.Write((byte)(sizes[i] % 256)); writer.Write((byte)(sizes[i] % 256)); writer.Write((ushort)0);
                writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(frames[i].Length); writer.Write(offset);
                offset += frames[i].Length;
            }
            foreach (byte[] frame in frames) writer.Write(frame);
        }
        return 0;
    }
}
