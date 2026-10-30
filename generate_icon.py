import math
from PIL import Image, ImageDraw, ImageFilter

def create_modern_icon():
    # 1024x1024 supersampled canvas
    size = 1024
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    # 1. Outer Squircle Drop Shadow
    shadow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    s_draw = ImageDraw.Draw(shadow)
    # Squircle boundary: 112 to 912 (800x800)
    b_min, b_max = 112, 912
    radius = 185
    s_draw.rounded_rectangle([b_min, b_min + 30, b_max, b_max + 36], radius=radius, fill=(0, 0, 0, 180))
    shadow = shadow.filter(ImageFilter.GaussianBlur(36))
    img.alpha_composite(shadow)

    # 2. Main Squircle Body (Dark Titanium / Obsidian)
    squircle = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    sq_draw = ImageDraw.Draw(squircle)
    # Smooth vertical gradient from dark gunmetal to pitch black
    for y in range(b_min, b_max + 1):
        ratio = (y - b_min) / (b_max - b_min)
        # Gradient: #222226 (34, 34, 38) down to #101012 (16, 16, 18)
        r = int(36 * (1 - ratio) + 14 * ratio)
        g = int(36 * (1 - ratio) + 14 * ratio)
        b = int(40 * (1 - ratio) + 16 * ratio)
        sq_draw.line([(b_min, y), (b_max, y)], fill=(r, g, b, 255))

    # Mask with rounded rectangle
    mask = Image.new("L", (size, size), 0)
    m_draw = ImageDraw.Draw(mask)
    m_draw.rounded_rectangle([b_min, b_min, b_max, b_max], radius=radius, fill=255)
    
    # Composite squircle onto main image using mask
    img.paste(squircle, (0, 0), mask)

    # 3. Outer Chamfered Rim (Subtle Metallic Silver border)
    rim = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    rim_draw = ImageDraw.Draw(rim)
    rim_draw.rounded_rectangle([b_min, b_min, b_max, b_max], radius=radius, outline=(255, 255, 255, 45), width=4)
    rim_draw.rounded_rectangle([b_min + 2, b_min + 2, b_max - 2, b_max - 2], radius=radius - 2, outline=(255, 255, 255, 20), width=2)
    img.alpha_composite(rim)

    # Top ambient light glare on the squircle
    glare = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    gl_draw = ImageDraw.Draw(glare)
    gl_draw.ellipse([b_min - 50, b_min - 200, b_max + 50, b_min + 250], fill=(255, 255, 255, 18))
    glare = glare.filter(ImageFilter.GaussianBlur(40))
    img.paste(Image.composite(glare, Image.new("RGBA", (size, size), (0, 0, 0, 0)), mask), (0, 0), mask)

    # 4. Central Dynamic Island Capsule (Jet Black Hardware Notch)
    # Capsule dimensions: centered horizontally, 640px wide, 240px tall
    cap_w, cap_h = 600, 230
    cap_x1 = (size - cap_w) // 2
    cap_y1 = (size - cap_h) // 2
    cap_x2 = cap_x1 + cap_w
    cap_y2 = cap_y1 + cap_h
    cap_r = cap_h // 2  # Full pill

    # Capsule subtle inner shadow & bezel
    draw = ImageDraw.Draw(img)
    # Outer bezel glow
    draw.rounded_rectangle([cap_x1 - 3, cap_y1 - 3, cap_x2 + 3, cap_y2 + 3], radius=cap_r + 3, outline=(50, 50, 58, 255), width=3)
    # Deep pitch black pill
    draw.rounded_rectangle([cap_x1, cap_y1, cap_x2, cap_y2], radius=cap_r, fill=(5, 5, 6, 255))

    # Inner glass top highlight
    highlight = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    h_draw = ImageDraw.Draw(highlight)
    h_draw.rounded_rectangle([cap_x1 + 6, cap_y1 + 4, cap_x2 - 6, cap_y1 + 45], radius=22, fill=(255, 255, 255, 25))
    highlight = highlight.filter(ImageFilter.GaussianBlur(6))
    img.alpha_composite(highlight)

    # 5. Inside the Capsule: Authentic Minimalist Audio Waveform & Camera Eye (Silver / Frost White)
    # Left: Camera eye (ultra subtle dark blue/gray iris with silver reflection)
    cam_cx, cam_cy = cap_x1 + 110, (cap_y1 + cap_y2) // 2
    cam_r = 34
    draw.ellipse([cam_cx - cam_r, cam_cy - cam_r, cam_cx + cam_r, cam_cy + cam_r], fill=(18, 18, 22, 255), outline=(38, 38, 44, 255), width=2)
    draw.ellipse([cam_cx - 16, cam_cy - 16, cam_cx + 16, cam_cy + 16], fill=(10, 10, 14, 255))
    draw.ellipse([cam_cx - 8, cam_cy - 10, cam_cx - 2, cam_cy - 4], fill=(255, 255, 255, 120))  # Pinpoint lens glint

    # Right: Sleek, high-precision silver/white soundwave (5 symmetrical bars)
    # Center y = 512
    cy = (cap_y1 + cap_y2) // 2
    wave_x = [cap_x1 + 270, cap_x1 + 330, cap_x1 + 390, cap_x1 + 450, cap_x1 + 510]
    wave_h = [48, 96, 140, 84, 42]
    bar_width = 18
    bar_radius = bar_width // 2

    # Draw soft white glow for waveform
    wave_glow = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    wg_draw = ImageDraw.Draw(wave_glow)
    for bx, bh in zip(wave_x, wave_h):
        by1 = cy - (bh // 2)
        by2 = cy + (bh // 2)
        wg_draw.rounded_rectangle([bx - 6, by1 - 6, bx + bar_width + 6, by2 + 6], radius=bar_radius + 6, fill=(255, 255, 255, 60))
    wave_glow = wave_glow.filter(ImageFilter.GaussianBlur(10))
    img.alpha_composite(wave_glow)

    # Draw crisp silver/white bars
    draw = ImageDraw.Draw(img)
    for bx, bh in zip(wave_x, wave_h):
        by1 = cy - (bh // 2)
        by2 = cy + (bh // 2)
        draw.rounded_rectangle([bx, by1, bx + bar_width, by2], radius=bar_radius, fill=(242, 242, 247, 255))

    # 6. Save as Multi-Resolution .ICO (256, 128, 64, 48, 32, 16)
    sizes = [(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)]
    icon_images = []
    for s in sizes:
        resized = img.resize(s, Image.Resampling.LANCZOS)
        icon_images.append(resized)

    out_path = r"D:\Projek-dev\aia-ide\app.ico"
    icon_images[0].save(out_path, format="ICO", sizes=sizes)
    print(f"Modern Icon successfully generated at: {out_path}")

if __name__ == "__main__":
    create_modern_icon()
