INSERT INTO public.themes (id, nombre, autor, descripcion, caracteristicas, video_demo, link_descarga, image_url)
VALUES
-- Pixel Prime
(
  gen_random_uuid(),
  'Pixel Prime',
  'PixeliGer',
  'Tema para Open PS2 Loader basado en la interfaz de Amazon Prime Video. Diseño minimalista y moderno con múltiples variantes de layout (sidebar, top bar, accent).',
  '["Diseño minimalista","Múltiples layouts (Sidebar, Top Bar, Accent)","Fondos de juego transparentes","Soporte HD/Full-HD 16:9 y 4:3","Compatible OPL 1.2+","Variantes de tema incluidas"]'::jsonb,
  NULL,
  'https://github.com/PixeliGer/OPL-Theme-Pixel-Prime/releases/latest',
  'https://raw.githubusercontent.com/PixeliGer/OPL-Theme-Pixel-Prime/main/assets/screenshots/screenshot_1.png'
),
-- Ominence
(
  gen_random_uuid(),
  'Ominence',
  'PixeliGer',
  'Tema para Open PS2 Loader inspirado en el elegante diseño de Eminence 2 para Kodi. Interfaz minimalista y sofisticada con 8 opciones de color y variantes Simple/Extended.',
  '["Diseño minimalista y compacto","8 opciones de color","Variantes Simple y Extended","Mostrar datos de juegos","Soporte HD/Full-HD 16:9 y 4:3","Compatible OPL 1.2+","Soporte MMCE/MX4SIO/USB"]'::jsonb,
  NULL,
  'https://github.com/PixeliGer/OPL-Theme-Ominence/releases/latest',
  'https://raw.githubusercontent.com/PixeliGer/OPL-Theme-Ominence/main/assets/screenshots/screenshot_1.png'
),
-- DeckyOS
(
  gen_random_uuid(),
  'DeckyOS',
  'PixeliGer',
  'Tema para Open PS2 Loader basado en la interfaz de SteamOS. Diseño minimalista con variantes de color Neon Pink y Neon Cyan.',
  '["Diseño minimalista inspirado en SteamOS","Variantes: Default, Neon Pink, Neon Cyan","Fondos de juego transparentes","Soporte HD/Full-HD 16:9 y 4:3","Compatible OPL 1.2+","Soporte MMCE/MX4SIO/USB"]'::jsonb,
  NULL,
  'https://github.com/PixeliGer/OPL-Theme-DeckyOS/releases/latest',
  'https://raw.githubusercontent.com/PixeliGer/OPL-Theme-DeckyOS/main/assets/screenshots/screenshot_1.png'
),
-- OPLAdvance
(
  gen_random_uuid(),
  'OPLAdvance',
  'PixeliGer',
  'Tema para Open PS2 Loader que rinde homenaje al clásico USBAdvance. Diseño retro-moderno que combina nostalgia con tecnología moderna.',
  '["Diseño retro-moderno","Homage a USBAdvance clásico","Soporte HD/Full-HD 16:9 y 4:3","Compatible OPL 1.2+","Soporte MMCE/MX4SIO/USB","Pantalla de info con detalles de juegos"]'::jsonb,
  NULL,
  'https://github.com/PixeliGer/OPL-Theme-OPLAdvance/releases/latest',
  'https://raw.githubusercontent.com/PixeliGer/OPL-Theme-OPLAdvance/main/assets/screenshots/screenshot_1.png'
),
-- PadOS
(
  gen_random_uuid(),
  'PadOS',
  'PixeliGer',
  'Tema para Open PS2 Loader basado en la interfaz de iPad iOS. Diseño elegante y minimalista con widgets y barra de navegación estilo dock.',
  '["Diseño inspirado en iPad iOS","Widgets principales y single widget","Barra de navegación estilo dock","Versiones 4:3 y Widescreen","Compatible OPL 1.2+","Soporte HD/Full-HD"]'::jsonb,
  NULL,
  'https://github.com/PixeliGer/OPL-Theme-PadOS/releases/latest',
  'https://raw.githubusercontent.com/PixeliGer/OPL-Theme-PadOS/main/assets/screenshots/screenshot1.png'
);
