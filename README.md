# Night Vision

Space Engineers (version 1) client plugin that adds a toggleable night vision mode in the style of
Elite Dangerous. It is built into the suit helmet and works passively through glass.

## Look

- Amplified luminance with the color dropped, so dark areas become readable.
- A wide, short-range infrared flood reveals geometry when no visible light remains.
- A monochrome teal-cyan tint by default.
- Thin bright contour lines on silhouettes, horizons and real steps between surfaces, from an
  edge detect on depth. Crease and panel lines from the normals, such as the edges of every
  armor block, are off by default and can be mixed in. Grass, bushes and trees do not receive them.
- The sky gets little amplification, so space stays black with the stars showing.
- Surfaces lit well enough by sunlight, ambient light or local lights keep their natural color.
- Grain and a vignette.
- Bright light sources overload the sensor and bloom instead of clipping.
- A short fade and flash when it switches on and off.

Tint, gain, fallback flood strength, sky gain, the natural light threshold, outline strength,
crease lines, grain, vignette, washout and fade time are set in the plugin's Settings.

## When it applies

| Situation | Source | Night vision |
|---|---|---|
| First person, helmet closed | Full | Full screen, except the HUD |
| First person, helmet open | Passive | Only through a clear-to-dark pair of `GLASS` surfaces |
| Third person, remote control, turrets, cameras, spectator | none | Normal rendering |

Activating night vision only arms it; the table decides how it renders. The HUD and GUI are never
processed.

### HUD indicator

While night vision is active, its icon replaces the main light glyph in the current HUD definition.
It is bright in full mode and uses the flashlight's disabled tone in passive mode. The light's
bottom bar remains independent and still shows whether the light itself is on or off.
The current HUD supplies the position, size, visibility and fading; no additional state icon or
HUD-mod-specific layout handling is added. Nothing is synced and there is no gameplay effect.

### Glass

Passive mode uses clear-to-dark transmission through the game's `GLASS` render technique. It therefore
follows actual one-way glass pixels in vanilla, DLC and modded models without cockpit definitions or
subtype lists. A clear-only material does not count, and a nearer dark face blocks clear faces behind it.

## Activation

Holding the light key past the configurable threshold toggles night vision and leaves the light
alone; a tap toggles the light as in vanilla. The key is whatever the light control is bound to (L
by default), so rebinding the light key moves it too. The gamepad and spectator light bindings are
left as vanilla.

## How it works

- `NightVisionController` runs on the game thread once per drawn frame, from a postfix on
  `MyGuiScreenGamePlay.Draw`. It decides the source, animates the fade and publishes a snapshot.
- `NightVisionRenderer` runs a pixel shader over the HDR light buffer from postfixes on
  `MyEyeAdaptation.Run` and `MyEyeAdaptation.ConstantExposure`, so bloom and tone mapping see the
  amplified image. It works in exposed space, so the look does not depend on what eye adaptation
  settles on.
- `InputHandler` rewrites the answer of `MyControllerHelper.IsControl(context, HEADLIGHTS)` while
  `MyGuiScreenGamePlay.HandleUnhandledInput` runs, which keeps vanilla's click sound and replay
  record consistent with what actually happened.
- The shader in `ClientPlugin/Shaders` is compiled by the game's shader registry after its includes
  are inlined, because the include callback does not work in the Linux build of the compiler.

## Building

See the [client plugin template](https://github.com/CometWorks/client-plugin-template) for the
build setup (`Directory.Build.props`, `setup.py`, deployment into Pulsar). Pulsar builds the plugin
from source and copies the shader folder as an asset.

## Support

Please report bugs on the Pulsar Discord: https://discord.gg/z8ZczP2YZY
