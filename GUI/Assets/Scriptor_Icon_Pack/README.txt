Scriptor Icon Pack v2
====================

Optimized around the simplified Scriptor logo for better readability at
Windows taskbar, title-bar, Explorer, and shortcut sizes.

Included:
- Scriptor.ico: multi-resolution Windows executable icon
- PNG assets: 16, 20, 24, 32, 40, 48, 64, 128, 256, 512 px
- Scriptor_Master_Compact.png: high-resolution source

Visual Studio / .NET
--------------------
Place Scriptor.ico in an Assets folder and add:

<PropertyGroup>
  <ApplicationIcon>Assets\Scriptor.ico</ApplicationIcon>
</PropertyGroup>

WPF window:
    Icon="Assets/Scriptor.ico"

WinForms:
Set the Form Icon property to Scriptor.ico.

The ICO contains multiple embedded resolutions so Windows can select an
appropriate image rather than scaling one large bitmap.
