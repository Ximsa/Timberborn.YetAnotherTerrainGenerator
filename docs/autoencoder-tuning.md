# Autoencoder generator tuning (2026-11-07)

The autoencoder generator's maps looked fine but lacked terrain variance. Measured with `tools/AutoencoderTuner`
on the 22 built-in maps, through the same pipeline as the generator (256 x 256, zoom 1.25, median 4 x 1):

| | Diversity (how different maps are) | Relief (mean step between tiles, in tiles) |
|---|---|---|
| Built-in maps | 0.258 | 0.271 |
| Before (multiplier -1, uniform noise 1) | 0.181 | 0.138 |
| New sampling (variety 1.5) | 0.183 | 0.150 |
| New sampling + sharpened decoder (shipped) | 0.157 | 0.239 |

Findings:

- The decoder blurs: reconstructions keep about 65% of the height variation of the built-in maps.
- The old noise (uniform, standard deviation about 0.29) was small next to the spread of the built-in maps'
  latents (about 0.5), and the multiplier of -1 kept the current map dominant: generations kept bringing back
  its features (e.g. Diorama's crater rim).
- Several latent numbers recall features of specific built-in maps (Craters' ridges, a ring valley), so the
  model blends remembered maps more than it composes new terrain.

Changes:

1. **Sampling.** `Models/LatentStats.txt` holds each latent number's mean and standard deviation over the
   built-in maps. The generator samples `mean + keep * (current - mean) + variety * std * gaussian`, with
   "Keep current map" (0..1, default 0) and "Variety" (0..3, default 1.5) replacing the old multiplier and noise
   sliders.
2. **Sharpened decoder.** An unsharp mask (amount 2, sigma 4 tiles of the 128 x 128 output) is baked into the
   decoder's last layer. That layer is linear, so this sharpens every output exactly as a filter afterwards
   would, at no cost when generating. NumSharp reads the rewritten file bit-identically (checked).

Reproduce from the untuned models (commit `d1a3362`):

```
dotnet run --project tools/AutoencoderTuner -c Release -- stats --maps <built-in maps> --out Models/LatentStats.txt
dotnet run --project tools/AutoencoderTuner -c Release -- sharpen --in Models/Decoder2D.npz --out Models/Decoder2D.npz --amount 2 --sigma 4
dotnet run --project tools/AutoencoderTuner -c Release -- preview --maps <built-in maps> --variety 1.5 --out preview.png
```

The stats are computed with the untuned encoder, so they stay valid after sharpening the decoder.
Not tested in game yet.

## Output filter: Gaussian instead of median (2026-11-07)

The generator enlarges the decoder's 128 x 128 output to the map by nearest neighbour, which leaves square blocks of
2-3 tiles. A median filter (one pass, radius 4, with sliders) used to hide them. It is replaced by a fixed Gaussian
blur (`Generators/Autoencoder/GaussianBlur.cs`, sigma 1.5 tiles, clamped edges) without sliders:

| Output filter | Diversity | Relief |
|---|---|---|
| Median, radius 4 (before) | 0.157 | 0.239 |
| None (block grid visible) | 0.149 | 0.399 |
| Gaussian, sigma 1.0 | 0.148 | 0.322 |
| **Gaussian, sigma 1.5 (shipped)** | 0.151 | 0.280 |
| Gaussian, sigma 2.0 | 0.154 | 0.252 |
| Built-in maps | 0.258 | 0.271 |

Sigma 1.5 removes the block grid and matches the built-in maps' relief, keeping finer shapes than the median.
The tuner's `preview` takes `--sigma` and, to reproduce the older numbers, `--median-passes 1`.
