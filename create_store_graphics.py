import os
import math
from PIL import Image, ImageDraw, ImageFont, ImageFilter

def create_store_graphics():
    out_dir = r"D:\Projek-dev\aia-ide\store_assets"
    os.makedirs(out_dir, exist_ok=True)

    # -------------------------------------------------------------
    # Helper to draw the icon squircle on any canvas
    # -------------------------------------------------------------
    def draw_app_icon(target_size=512):
        size = 1024
        img = Image.new("RGBA", (size, size), (0, 0, 0, 0))

        # Shadow
        shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        s_draw = ImageDraw.Draw(shadow)
        b_min, b_max = 112, 912
        radius = 185
        s_draw.rounded_rectangle([b_min, b_min + 30, b_max, b_max + 36], radius=radius, fill=(0, 0, 0, 180))
        shadow = shadow.filter(ImageFilter.GaussianBlur(36))
        img.alpha_composite(shadow)

        # Main Squircle Body
        squircle = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        sq_draw = ImageDraw.Draw(squircle)
        for y in range(b_min, b_max + 1):
            ratio = (y - b_min) / (b_max - b_min)
            r = int(36 * (1 - ratio) + 14 * ratio)
            g = int(36 * (1 - ratio) + 14 * ratio)
            b = int(40 * (1 - ratio) + 16 * ratio)
            sq_draw.line([(b_min, y), (b_max, y)], fill=(r, g, b, 255))

        mask = Image.new("L", (size, size), 0)
        m_draw = ImageDraw.Draw(mask)
        m_draw.rounded_rectangle([b_min, b_min, b_max, b_max], radius=radius, fill=255)
        img.paste(squircle, (0, 0), mask)

        # Rim
        rim = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        rim_draw = ImageDraw.Draw(rim)
        rim_draw.rounded_rectangle([b_min, b_min, b_max, b_max], radius=radius, outline=(255, 255, 255, 45), width=4)
        rim_draw.rounded_rectangle([b_min + 2, b_min + 2, b_max - 2, b_max - 2], radius=radius - 2, outline=(255, 255, 255, 20), width=2)
        img.alpha_composite(rim)

        # Glare
        glare = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        gl_draw = ImageDraw.Draw(glare)
        gl_draw.ellipse([b_min - 50, b_min - 200, b_max + 50, b_min + 250], fill=(255, 255, 255, 18))
        glare = glare.filter(ImageFilter.GaussianBlur(40))
        img.paste(Image.composite(glare, Image.new("RGBA", (size, size), (0, 0, 0, 0)), mask), (0, 0), mask)

        # Capsule
        cap_w, cap_h = 600, 230
        cap_x1 = (size - cap_w) // 2
        cap_y1 = (size - cap_h) // 2
        cap_x2 = cap_x1 + cap_w
        cap_y2 = cap_y1 + cap_h
        cap_r = cap_h // 2

        draw = ImageDraw.Draw(img)
        draw.rounded_rectangle([cap_x1 - 3, cap_y1 - 3, cap_x2 + 3, cap_y2 + 3], radius=cap_r + 3, outline=(50, 50, 58, 255), width=3)
        draw.rounded_rectangle([cap_x1, cap_y1, cap_x2, cap_y2], radius=cap_r, fill=(5, 5, 6, 255))

        highlight = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        h_draw = ImageDraw.Draw(highlight)
        h_draw.rounded_rectangle([cap_x1 + 6, cap_y1 + 4, cap_x2 - 6, cap_y1 + 45], radius=22, fill=(255, 255, 255, 25))
        highlight = highlight.filter(ImageFilter.GaussianBlur(6))
        img.alpha_composite(highlight)

        # Eye & Waveform
        cam_cx, cam_cy = cap_x1 + 110, (cap_y1 + cap_y2) // 2
        cam_r = 34
        draw.ellipse([cam_cx - cam_r, cam_cy - cam_r, cam_cx + cam_r, cam_cy + cam_r], fill=(18, 18, 22, 255), outline=(38, 38, 44, 255), width=2)
        draw.ellipse([cam_cx - 16, cam_cy - 16, cam_cx + 16, cam_cy + 16], fill=(10, 10, 14, 255))
        draw.ellipse([cam_cx - 8, cam_cy - 10, cam_cx - 2, cam_cy - 4], fill=(255, 255, 255, 120))

        cy = (cap_y1 + cap_y2) // 2
        wave_x = [cap_x1 + 270, cap_x1 + 330, cap_x1 + 390, cap_x1 + 450, cap_x1 + 510]
        wave_h = [48, 96, 140, 84, 42]
        bar_width = 18
        bar_radius = bar_width // 2

        wave_glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        wg_draw = ImageDraw.Draw(wave_glow)
        for bx, bh in zip(wave_x, wave_h):
            by1 = cy - (bh // 2)
            by2 = cy + (bh // 2)
            wg_draw.rounded_rectangle([bx - 6, by1 - 6, bx + bar_width + 6, by2 + 6], radius=bar_radius + 6, fill=(255, 255, 255, 60))
        wave_glow = wave_glow.filter(ImageFilter.GaussianBlur(10))
        img.alpha_composite(wave_glow)

        draw = ImageDraw.Draw(img)
        for bx, bh in zip(wave_x, wave_h):
            by1 = cy - (bh // 2)
            by2 = cy + (bh // 2)
            draw.rounded_rectangle([bx, by1, bx + bar_width, by2], radius=bar_radius, fill=(242, 242, 247, 255))

        return img.resize((target_size, target_size), Image.Resampling.LANCZOS)

    # -------------------------------------------------------------
    # 1. Box Art 1080 x 1080 (1:1 Square Required for Store)
    # -------------------------------------------------------------
    box_art = Image.new("RGB", (1080, 1080), (14, 14, 18))
    b_draw = ImageDraw.Draw(box_art)
    # Subtle ambient gradient glow in center
    glow = Image.new("RGBA", (1080, 1080), (0, 0, 0, 0))
    g_draw = ImageDraw.Draw(glow)
    g_draw.ellipse([240, 240, 840, 840], fill=(0, 122, 255, 35))
    glow = glow.filter(ImageFilter.GaussianBlur(90))
    box_art.paste(glow, (0, 0), glow)

    # Paste 880x880 Icon in center
    icon_880 = draw_app_icon(880)
    box_art.paste(icon_880, ((1080 - 880) // 2, (1080 - 880) // 2), icon_880)
    box_path = os.path.join(out_dir, "BoxArt_1080x1080.png")
    box_art.save(box_path, "PNG")
    print(f"Generated: {box_path}")

    # -------------------------------------------------------------
    # 2. Poster Art 720 x 1080 (2:3 Portrait Poster)
    # -------------------------------------------------------------
    poster = Image.new("RGB", (720, 1080), (12, 12, 16))
    p_draw = ImageDraw.Draw(poster)
    # Background radial neon accent
    p_glow = Image.new("RGBA", (720, 1080), (0, 0, 0, 0))
    pg_draw = ImageDraw.Draw(p_glow)
    pg_draw.ellipse([110, 180, 610, 680], fill=(0, 122, 255, 45))
    p_glow = p_glow.filter(ImageFilter.GaussianBlur(80))
    poster.paste(p_glow, (0, 0), p_glow)

    # Icon in upper-middle (520x520)
    icon_520 = draw_app_icon(520)
    poster.paste(icon_520, ((720 - 520) // 2, 200), icon_520)

    # Typography
    try:
        font_large = ImageFont.truetype("segoeui.ttf", 54)
        font_sub = ImageFont.truetype("segoeui.ttf", 26)
        font_brand = ImageFont.truetype("segoeuib.ttf", 22)
    except:
        font_large = font_sub = font_brand = ImageFont.load_default()

    p_draw.text((360, 780), "Aia Dinamic", fill=(255, 255, 255), font=font_large, anchor="mm")
    p_draw.text((360, 840), "Dynamic Island for Windows", fill=(160, 160, 175), font=font_sub, anchor="mm")
    p_draw.text((360, 960), "BY AIASTUDIO", fill=(0, 150, 255), font=font_brand, anchor="mm")

    poster_path = os.path.join(out_dir, "PosterArt_720x1080.png")
    poster.save(poster_path, "PNG")
    print(f"Generated: {poster_path}")

    # -------------------------------------------------------------
    # 3. Four Professional Store Screenshots (1920 x 1080 Full HD)
    # -------------------------------------------------------------
    def create_base_desktop(theme_color=(20, 24, 38)):
        desktop = Image.new("RGB", (1920, 1080), theme_color)
        d_draw = ImageDraw.Draw(desktop)
        # Windows 11 style abstract bloom wallpaper
        for i in range(5):
            rad = 300 + i * 80
            cx = 960 + int(math.sin(i) * 200)
            cy = 540 + int(math.cos(i) * 100)
            glow_l = Image.new("RGBA", (1920, 1080), (0, 0, 0, 0))
            gl_d = ImageDraw.Draw(glow_l)
            color = (30 + i * 20, 60 + i * 25, 120 + i * 20, 50 - i * 8)
            gl_d.ellipse([cx - rad, cy - rad, cx + rad, cy + rad], fill=color)
            glow_l = glow_l.filter(ImageFilter.GaussianBlur(100))
            desktop.paste(glow_l, (0, 0), glow_l)

        # Windows 11 Taskbar at bottom
        tb_h = 48
        d_draw.rectangle([0, 1080 - tb_h, 1920, 1080], fill=(22, 22, 26, 220))
        # Taskbar start icons in center
        start_x = 960 - 100
        for idx in range(6):
            d_draw.rounded_rectangle([start_x + idx * 36, 1080 - 38, start_x + idx * 36 + 26, 1080 - 12], radius=6, fill=(50, 50, 60))

        return desktop

    # Screenshot 1: Compact Dynamic Island (Music Playing)
    sc1 = create_base_desktop()
    s1_draw = ImageDraw.Draw(sc1)
    # Notch capsule at top center: 230px wide, 38px tall
    nw, nh = 230, 38
    nx1 = (1920 - nw) // 2
    nx2 = nx1 + nw
    # Draw dark notch
    s1_draw.rounded_rectangle([nx1, 0, nx2, nh], radius=nh // 2, fill=(12, 12, 14))
    s1_draw.rounded_rectangle([nx1, 0, nx2, nh], radius=nh // 2, outline=(55, 55, 65), width=1)
    # Mini vinyl & animated waveform in compact notch
    s1_draw.ellipse([nx1 + 12, 7, nx1 + 36, 31], fill=(24, 24, 28), outline=(60, 60, 70))
    s1_draw.ellipse([nx1 + 20, 15, nx1 + 28, 23], fill=(0, 150, 255))
    # 4 equalizer mini bars
    eq_x = nx2 - 40
    for idx, h in enumerate([10, 18, 14, 20]):
        s1_draw.rounded_rectangle([eq_x + idx * 6, nh - 9 - h, eq_x + idx * 6 + 4, nh - 9], radius=2, fill=(48, 209, 88))

    # Add Showcase Feature Label at bottom
    try:
        f_title = ImageFont.truetype("segoeuib.ttf", 46)
        f_sub = ImageFont.truetype("segoeui.ttf", 24)
    except:
        f_title = f_sub = ImageFont.load_default()

    s1_draw.text((960, 840), "Sleek Compact Dynamic Island", fill=(255, 255, 255), font=f_title, anchor="mm")
    s1_draw.text((960, 890), "Discreetly docked at the top bezel with live media & volume tracking", fill=(180, 185, 205), font=f_sub, anchor="mm")
    sc1.save(os.path.join(out_dir, "Screenshot_1_CompactNotch.png"), "PNG")
    print("Generated: Screenshot_1_CompactNotch.png")

    # Screenshot 2: Expanded Live 20-Band Equalizer Visualizer
    sc2 = create_base_desktop((25, 20, 35))
    s2_draw = ImageDraw.Draw(sc2)
    # Expanded card: 620px wide, 240px tall
    ew, eh = 620, 240
    ex1 = (1920 - ew) // 2
    ex2 = ex1 + ew
    s2_draw.rounded_rectangle([ex1, 0, ex2, eh], radius=32, fill=(16, 16, 20))
    s2_draw.rounded_rectangle([ex1, 0, ex2, eh], radius=32, outline=(65, 65, 80), width=1)
    # Album art vinyl
    s2_draw.ellipse([ex1 + 36, 50, ex1 + 176, 190], fill=(22, 22, 26), outline=(70, 70, 85), width=2)
    s2_draw.ellipse([ex1 + 86, 100, ex1 + 126, 140], fill=(255, 45, 85))
    # 20-Band EQ bars
    eq_start = ex1 + 210
    heights = [18, 30, 48, 65, 82, 70, 54, 40, 58, 75, 90, 85, 68, 52, 45, 60, 72, 58, 42, 28]
    for idx, bh in enumerate(heights):
        s2_draw.rounded_rectangle([eq_start + idx * 18, 175 - bh, eq_start + idx * 18 + 12, 175], radius=6, fill=(0, 180, 255))

    s2_draw.text((960, 840), "Real-Time 20-Band Studio Equalizer", fill=(255, 255, 255), font=f_title, anchor="mm")
    s2_draw.text((960, 890), "Hardware WASAPI audio visualizer tuned with snappy logarithmic ballistics", fill=(180, 185, 205), font=f_sub, anchor="mm")
    sc2.save(os.path.join(out_dir, "Screenshot_2_AudioSpectrum.png"), "PNG")
    print("Generated: Screenshot_2_AudioSpectrum.png")

    # Screenshot 3: Real-Time Synchronized Karaoke Lyrics
    sc3 = create_base_desktop((18, 26, 32))
    s3_draw = ImageDraw.Draw(sc3)
    s3_draw.rounded_rectangle([ex1, 0, ex2, 260], radius=32, fill=(14, 14, 18))
    s3_draw.rounded_rectangle([ex1, 0, ex2, 260], radius=32, outline=(55, 65, 85), width=1)
    # Lyrics lines
    s3_draw.text((ex1 + 50, 70), "♪ Never gonna give you up", fill=(130, 135, 150), font=f_sub)
    s3_draw.text((ex1 + 50, 125), "Never gonna let you down", fill=(0, 210, 255), font=f_title)
    s3_draw.text((ex1 + 50, 195), "Never gonna run around and desert you", fill=(130, 135, 150), font=f_sub)

    s3_draw.text((960, 840), "Apple Music Sing-Style Live Karaoke Lyrics", fill=(255, 255, 255), font=f_title, anchor="mm")
    s3_draw.text((960, 890), "Auto-fetching synced lyrics with adjustable font sizes up to 72pt", fill=(180, 185, 205), font=f_sub, anchor="mm")
    sc3.save(os.path.join(out_dir, "Screenshot_3_SyncedLyrics.png"), "PNG")
    print("Generated: Screenshot_3_SyncedLyrics.png")

    # Screenshot 4: Quick App Dock & System Dashboard
    sc4 = create_base_desktop((20, 20, 28))
    s4_draw = ImageDraw.Draw(sc4)
    s4_draw.rounded_rectangle([ex1, 0, ex2, 210], radius=32, fill=(16, 16, 20))
    s4_draw.rounded_rectangle([ex1, 0, ex2, 210], radius=32, outline=(60, 60, 75), width=1)
    # Dock shortcuts icons
    dock_x = ex1 + 60
    app_colors = [(0, 122, 255), (48, 209, 88), (255, 159, 10), (175, 82, 222), (255, 59, 48), (255, 214, 10)]
    app_names = ["Chrome", "Spotify", "VS Code", "Terminal", "Files", "Settings"]
    for idx, (col, name) in enumerate(zip(app_colors, app_names)):
        cx = dock_x + idx * 85
        s4_draw.rounded_rectangle([cx, 60, cx + 58, 118], radius=16, fill=col)
        s4_draw.text((cx + 29, 140), name, fill=(200, 200, 215), font=f_sub, anchor="mm")

    s4_draw.text((960, 840), "Quick App Dock & System Dashboard", fill=(255, 255, 255), font=f_title, anchor="mm")
    s4_draw.text((960, 890), "Instantly launch your favorite tools and monitor CPU & RAM in real time", fill=(180, 185, 205), font=f_sub, anchor="mm")
    sc4.save(os.path.join(out_dir, "Screenshot_4_AppDock.png"), "PNG")
    print("Generated: Screenshot_4_AppDock.png")

if __name__ == "__main__":
    create_store_graphics()
