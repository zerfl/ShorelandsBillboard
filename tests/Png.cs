using System;
using System.IO;
using System.IO.Compression;

// 8-bit RGBA PNG, not interlaced: what art/make_billboard.py writes.
sealed class Png
{
    public int W, H;
    byte[] rgba;

    public byte A(int x, int y) => rgba[(y * W + x) * 4 + 3];
    public int Rgb(int x, int y) => rgba[(y * W + x) * 4] << 16 | rgba[(y * W + x) * 4 + 1] << 8 | rgba[(y * W + x) * 4 + 2];

    public static Png Read(string path)
    {
        var data = File.ReadAllBytes(path);
        var png = new Png();
        var idat = new MemoryStream();
        for (int at = 8; at < data.Length;)
        {
            int len = data[at] << 24 | data[at + 1] << 16 | data[at + 2] << 8 | data[at + 3];
            string type = System.Text.Encoding.ASCII.GetString(data, at + 4, 4);
            if (type == "IHDR")
            {
                png.W = data[at + 8] << 24 | data[at + 9] << 16 | data[at + 10] << 8 | data[at + 11];
                png.H = data[at + 12] << 24 | data[at + 13] << 16 | data[at + 14] << 8 | data[at + 15];
                if (data[at + 16] != 8 || data[at + 17] != 6 || data[at + 20] != 0) throw new Exception(path + ": not 8-bit RGBA, non-interlaced");
            }
            if (type == "IDAT") idat.Write(data, at + 8, len);
            at += 12 + len;
        }
        idat.Position = 0;
        var raw = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);
        var src = raw.ToArray();
        int stride = png.W * 4;
        png.rgba = new byte[stride * png.H];
        for (int y = 0; y < png.H; y++)
        {
            int filter = src[y * (stride + 1)];
            for (int i = 0; i < stride; i++)
            {
                int a = i >= 4 ? png.rgba[y * stride + i - 4] : 0, b = y > 0 ? png.rgba[(y - 1) * stride + i] : 0, c = i >= 4 && y > 0 ? png.rgba[(y - 1) * stride + i - 4] : 0;
                int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                int predict = filter == 0 ? 0 : filter == 1 ? a : filter == 2 ? b : filter == 3 ? (a + b) / 2 : pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
                png.rgba[y * stride + i] = (byte)(src[y * (stride + 1) + 1 + i] + predict);
            }
        }
        return png;
    }
}
