using System.IO.Compression;
using System.Text;

namespace Timberborn.TerrainGenerator.Core.Autoencoder;

// The autoencoder's dense ReLU networks (three layers, PyTorch layout weight[out, in]) as stored in Models/*.npz,
// in plain C# so it runs the same in the game and in the offline tools.
public sealed class Mlp
{
    public const int Layers = 3;

    public float[][] Weights { get; } = new float[Layers][];
    public float[][] Biases { get; } = new float[Layers][];
    public int[] Inputs { get; } = new int[Layers];
    public int[] Outputs { get; } = new int[Layers];

    public static Mlp Load(string path)
    {
        var mlp = new Mlp();
        using var file = File.OpenRead(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        for (var layer = 0; layer < Layers; layer++)
        {
            var (weights, shape) = ReadNpy(zip, $"{layer + 1}.weight.npy");
            mlp.Weights[layer] = weights;
            mlp.Outputs[layer] = shape[0];
            mlp.Inputs[layer] = shape[1];
            mlp.Biases[layer] = ReadNpy(zip, $"{layer + 1}.bias.npy").Data;
        }

        return mlp;
    }

    // writes the same entries as the original files (numpy's npz format)
    public void Save(string path)
    {
        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        WriteNpy(zip, "allow_pickle.npy", "|b1", Array.Empty<int>(), new byte[] { 0 });
        for (var layer = 0; layer < Layers; layer++)
        {
            WriteFloats(zip, $"{layer + 1}.weight.npy", new[] { Outputs[layer], Inputs[layer] }, Weights[layer]);
            WriteFloats(zip, $"{layer + 1}.bias.npy", new[] { Outputs[layer] }, Biases[layer]);
        }
    }

    public float[] Run(float[] input)
    {
        var values = input;
        for (var layer = 0; layer < Layers; layer++)
            values = Layer(layer, values, layer < Layers - 1);
        return values;
    }

    private float[] Layer(int layer, float[] input, bool relu)
    {
        var output = new float[Outputs[layer]];
        var weights = Weights[layer];
        var inputs = Inputs[layer];
        Parallel.For(0, output.Length, o =>
        {
            double sum = Biases[layer][o];
            var offset = o * inputs;
            for (var i = 0; i < inputs; i++)
                sum += weights[offset + i] * input[i];
            output[o] = relu && sum < 0 ? 0 : (float)sum;
        });
        return output;
    }

    private static (float[] Data, int[] Shape) ReadNpy(ZipArchive zip, string name)
    {
        using var stream = zip.GetEntry(name)!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        var bytes = memory.ToArray();
        var headerLength = BitConverter.ToUInt16(bytes, 8);
        var header = Encoding.ASCII.GetString(bytes, 10, headerLength);
        if (!header.Contains("'<f4'") || header.Contains("'fortran_order': True"))
            throw new InvalidDataException($"{name}: expected little-endian float32 in C order, got {header}");
        var shapeText = header[(header.IndexOf("'shape': (", StringComparison.Ordinal) + 10)..];
        shapeText = shapeText[..shapeText.IndexOf(')')];
        var shape = shapeText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim())
            .Select(int.Parse).ToArray();
        var data = new float[(bytes.Length - 10 - headerLength) / 4];
        Buffer.BlockCopy(bytes, 10 + headerLength, data, 0, data.Length * 4);
        return (data, shape);
    }

    private static void WriteFloats(ZipArchive zip, string name, int[] shape, float[] data)
    {
        var bytes = new byte[data.Length * 4];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        WriteNpy(zip, name, "<f4", shape, bytes);
    }

    // npy format 1.0: magic, version, header length, a header dict padded so the data starts on 64 bytes
    private static void WriteNpy(ZipArchive zip, string name, string type, int[] shape, byte[] data)
    {
        var shapeText = shape.Length switch
        {
            0 => "()",
            1 => $"({shape[0]},)",
            _ => $"({string.Join(", ", shape)})"
        };
        var header = $"{{'descr': '{type}', 'fortran_order': False, 'shape': {shapeText}, }}";
        var padding = 64 - (10 + header.Length + 1) % 64;
        header += new string(' ', padding % 64) + "\n";
        using var stream = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
        stream.Write(new byte[] { 0x93, (byte)'N', (byte)'U', (byte)'M', (byte)'P', (byte)'Y', 1, 0 });
        stream.Write(BitConverter.GetBytes((ushort)header.Length));
        stream.Write(Encoding.ASCII.GetBytes(header));
        stream.Write(data);
    }
}
