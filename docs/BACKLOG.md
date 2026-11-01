# Backlog

- [ ] **Renegade Games startup logo.** Replace the Godot logo shown at launch (requested 2026-11-01; Pest Control has it working).
  - Copy `C:\GameDev\Branding\renegade-games-splash-portrait.png` to `assets/splash/renegade_games.png`. It's the portrait crop; `renegade-games-splash.png` is the wide one for landscape games.
  - In `project.godot`, under `[application]`, set:
    ```
    boot_splash/bg_color=Color(0.1294, 0.1294, 0.1373, 1)
    boot_splash/image="res://assets/splash/renegade_games.png"
    boot_splash/minimum_display_time=1500
    ```
  - Check on the phone that the logo shows at launch, then bump the version code for the next upload.
  - **Decide before shipping:** this game is listed for everyone including kids (Families policy, child-directed ads, Teacher Approved), and the logo is a skeleton with two revolvers, so far kept off the icon and listing. Settle whether it runs in full, or as a version without the figure, and whether the content rating answers still hold.
