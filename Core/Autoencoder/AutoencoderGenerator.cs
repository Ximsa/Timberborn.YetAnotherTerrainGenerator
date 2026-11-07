namespace Timberborn.TerrainGenerator.Core.Autoencoder;

public sealed class AutoencoderSettings
{
    // 0 ignores the current map, 1 keeps its latent and only adds variety
    public float KeepCurrent;

    // noise in standard deviations of the built-in maps' latents
    public float Variety = 1.5f;

    // the decoded map is enlarged by this factor and a random part of it is used
    public float ZoomFactor = 1.25f;

    // Gaussian blur on the enlarged map, in tiles
    public double BlurSigma = GaussianBlur.DefaultSigma;

    // stretch the result so its lowest column becomes 0 and its highest 1; otherwise heights stay on the scale the
    // encoder saw, where 1 is the current map's highest column
    public bool Normalize = true;

    public AutoencoderSettings Clone()
    {
        return (AutoencoderSettings)MemberwiseClone();
    }
}

// The autoencoder generator: encodes the current map into a few latent numbers, samples new ones around the
// built-in maps' latents, decodes them into a small heightmap and enlarges that to the map. Used by the game's dialog
// and by tools/AutoencoderTuner, so offline previews match the game.
public sealed class AutoencoderGenerator
{
    private readonly Mlp decoder;
    private readonly Mlp encoder;
    private readonly LatentStats stats;

    public AutoencoderGenerator(Mlp encoder, Mlp decoder, LatentStats stats)
    {
        this.encoder = encoder;
        this.decoder = decoder;
        this.stats = stats;
        InputSize = (int)Math.Sqrt(encoder.Inputs[0]);
    }

    // edge length of the square heightmaps the networks work on
    public int InputSize { get; }

    // Encoder2D.npz, Decoder2D.npz and LatentStats.txt from the models folder
    public static AutoencoderGenerator Load(string modelsFolder)
    {
        return new AutoencoderGenerator(Mlp.Load(Path.Combine(modelsFolder, "Encoder2D.npz")),
            Mlp.Load(Path.Combine(modelsFolder, "Decoder2D.npz")),
            LatentStats.Load(Path.Combine(modelsFolder, "LatentStats.txt")));
    }

    // a new map's heights in 0..1, indexed y * sizeX + x; currentHeights describe the current map the same way
    public float[] Generate(float[] currentHeights, int sizeX, int sizeY, AutoencoderSettings settings, Random random)
    {
        var latent = Sample(Encode(currentHeights, sizeX, sizeY), settings, random);
        return Finish(Decode(latent), sizeX, sizeY, settings, random);
    }

    // the current map's heights (any scale) as latent numbers
    public float[] Encode(float[] heights, int sizeX, int sizeY)
    {
        return encoder.Run(NormalizeInput(heights, sizeX, sizeY, InputSize));
    }

    // heights scaled to side x side by nearest neighbour and divided by their maximum, as the encoder expects
    public static float[] NormalizeInput(float[] heights, int sizeX, int sizeY, int side)
    {
        var input = Scale(heights, sizeX, sizeY, side, side);
        var max = Math.Max(1e-6f, input.Max());
        return input.Select(height => height / max).ToArray();
    }

    public float[] Sample(float[] current, AutoencoderSettings settings, Random random)
    {
        return stats.Sample(current, settings.KeepCurrent, settings.Variety, random);
    }

    // a square heightmap of InputSize x InputSize
    public float[] Decode(float[] latent)
    {
        return decoder.Run(latent);
    }

    // enlarges a decoded heightmap to the map (zoomed, a random part of it), blurs it and, if normalizing, stretches it
    // to 0..1
    public float[] Finish(float[] decoded, int sizeX, int sizeY, AutoencoderSettings settings, Random random,
        Action<float>? progress = null)
    {
        // enlarge to a square covering the map, unless the map is smaller than the decoded heightmap
        var side = Math.Max(InputSize, Math.Max(sizeX, sizeY));
        var map = Scale(decoded, InputSize, InputSize, side, side);
        var zoomed = (int)(side * settings.ZoomFactor);
        map = Scale(map, side, side, zoomed, zoomed);
        progress?.Invoke(0.5f);

        int offsetY = random.Next(zoomed - sizeY + 1), offsetX = random.Next(zoomed - sizeX + 1);
        var cropped = new float[sizeX * sizeY];
        for (var y = 0; y < sizeY; y++)
        for (var x = 0; x < sizeX; x++)
            cropped[y * sizeX + x] = map[(y + offsetY) * zoomed + x + offsetX];

        // round off the square blocks that enlarging by nearest neighbour leaves
        cropped = GaussianBlur.Apply(cropped, sizeX, sizeY, settings.BlurSigma);
        progress?.Invoke(0.9f);

        if (!settings.Normalize)
            return cropped;
        float min = cropped.Min(), range = Math.Max(1e-6f, cropped.Max() - min);
        var result = cropped.Select(height => (height - min) / range).ToArray();
        progress?.Invoke(1);
        return result;
    }

    // nearest neighbour, indexed y * width + x
    private static float[] Scale(float[] values, int fromX, int fromY, int toX, int toY)
    {
        var result = new float[toX * toY];
        for (var y = 0; y < toY; y++)
        for (var x = 0; x < toX; x++)
            result[y * toX + x] = values[(int)(y * (float)fromY / toY) * fromX + (int)(x * (float)fromX / toX)];
        return result;
    }
}
