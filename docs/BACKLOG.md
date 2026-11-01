# Backlog

- [ ] **Renegade Games startup logo.** Replace the Godot logo shown at launch (requested 2026-11-01; Pest Control has it working).
  - Copy `C:\GameDev\Branding\renegade-games-splash-friendly.png` to `assets/splash/renegade_games.png`. It's the kid-friendly logo: smiling skull, no guns or bullets. Don't use the other splash images in `Branding` here; they show revolvers and are for 13+ games.
  - In `project.godot`, under `[application]`, set:
    ```
    boot_splash/bg_color=Color(0.1647, 0.1686, 0.2039, 1)
    boot_splash/image="res://assets/splash/renegade_games.png"
    boot_splash/minimum_display_time=1500
    ```
  - Check on the phone that the logo shows at launch, then bump the version code for the next upload.
  - **Launch screen only:** this game is listed for everyone including kids (Families policy, child-directed ads, Teacher Approved), so keep the logo off the icon and store listing, and check that the content rating answers still hold before shipping.
