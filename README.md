# Night Vision

Space Engineers (version 1) client plugin that adds a toggleable night vision mode in the style of
Elite Dangerous. It is built into the suit helmet and into cockpit glass.

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
| First person on foot, helmet closed | Helmet | Full screen, except the HUD |
| First person on foot, helmet open | none | Standby, nothing is processed |
| Seated in first person, the block has glass | Cockpit | Only what is seen through the glass, any helmet state |
| Seated in first person, the block has no glass | Helmet | Same as on foot |
| Third person, remote control, turrets, cameras, spectator | none | Normal rendering |

Activating night vision only arms it; the table decides whether it renders. The toggle is shared
between the suit and the cockpit, so it follows the player in and out of seats. The HUD and GUI are never processed.

### HUD indicator

While night vision is active, its icon replaces the main light glyph in the current HUD definition.
The light's bottom bar remains independent and still shows whether the light itself is on or off.
The current HUD supplies the position, size, visibility and fading; no additional state icon or
HUD-mod-specific layout handling is added. Nothing is synced and there is no gameplay effect.

### Cockpit glass

Whether a seat has glass is decided once per block definition by looking for meshes with the
`GLASS` draw technique in its model, interior model, glass model and subparts. That scan gets a
few vanilla and DLC blocks wrong, which `GlassDetector` corrects with a built-in list: the rover
cockpit has windows but no `GLASS` meshes, while the open cockpits, the suspended control seats
and some furniture have glass that the occupant does not look through.
Modded blocks can be fixed with the two subtype lists in the Settings. Starting the game with
`NIGHTVISION_GLASS_REPORT=1` logs the detection result for every seat definition.

Seen through the glass means outside the block's box: pixels whose world position lies inside the
seat block (with its interior model) keep their normal look. The pilot's own grid outside that
box is processed too, since the depth buffer cannot tell it apart from the world.

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
