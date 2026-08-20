A directory of community-built plugins for ImageGlass. Currently, ImageGlass supports codec plugins to extend the list of compatible image formats.

**Legends**:
- Supported platforms: 🪟 Windows, 🍎 macOS, 🐧 Linux
- Plugin type:
  + Codec: Extends the image formats supported by ImageGlass.


## Available plugins
Explore the community plugins listed below.

> [!NOTE]
> Plugins execute with full permissions and can run arbitrary code on your system. Only install and enable third-party plugins from trusted sources and authors.


| Plugin | Author | Description | Platforms |
| :--- | :--- | :--- | :--- |
| [WIC Codec](https://github.com/d2phap/wic-imageglass-plugin) | [d2phap](https://github.com/d2phap) | Brings the Windows Imaging Component into ImageGlass as a native codec plugin including JPEG XR (`.jxr`, `.hdp`, `.wdp`) | 🪟 |
| [APNG Codec](https://github.com/d2phap/apng-imageglass-plugin) | [d2phap](https://github.com/d2phap) | Animated APNG codec plugin for ImageGlass | 🪟🍎🐧 |
| [ITHMB Codec](https://github.com/B67687/Imageglass-Ithmb-Plugin) | [B67687](https://github.com/B67687) | C ABI plugin to decode `.ithmb` thumbnail files using [Ithmb-Codec](https://github.com/B67687/Ithmb-Codec) | 🪟🍎🐧 |


## FAQs
### 1. How to install a plugin
1. Open ImageGlass Settings > Plugins.
2. Click the **Add** button and select your `.igplugin.zip` file.
3. Click on the plugin to open the **Plugin information** window.
4. Click **Trust and enable** to activate it.

<img height="500" src="https://github.com/user-attachments/assets/64b2ed14-8c2c-4b80-a02b-c98f4cc7ed8c" />


### 2. How to submit a plugin
1. Fork this repository https://github.com/ImageGlass/SDK.
2. Update this file ('showcase/plugins.md') with the plugin details under the correct category.
3. Submit a pull request.

