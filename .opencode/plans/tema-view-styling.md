# Plan: Mejoras de estilo en TemaView

## Cambios realizados

- [x] Fondo de la tarjeta: `#181C2C` → `#121212`
- [x] Esquinas de la imagen: `(12, 12, 0, 0)` → `(5)`

## Pendiente: Quitar botón "Ir" y hacer card clickable

**Archivo:** `PS2Desktop\Vistas\TemaView.xaml.cs` — método `CrearTarjetaTema()`

### 1. Agregar `Cursor` y `MouseDown` al `border`
Reemplazar:
```csharp
var border = new Border
{
    Width = 240,
    Margin = new Thickness(0, 0, 20, 30),
    Background = ...,
    CornerRadius = new CornerRadius(12),
    Tag = tema
};
```
Por:
```csharp
var border = new Border
{
    Width = 240,
    Margin = new Thickness(0, 0, 20, 30),
    Background = ...,
    CornerRadius = new CornerRadius(12),
    Cursor = System.Windows.Input.Cursors.Hand,
    Tag = tema
};
```

### 2. Agregar evento click al `border` (después de `border.Child = stackPanel;`)
```csharp
border.MouseDown += (s, e) => IrADetalle?.Invoke(this, tema);
```

### 3. Eliminar todo el bloque del botón "Ir" (líneas 180-196)
```csharp
// Botón Ir
var btnIr = new Button { ... };
btnIr.Click += (s, e) => IrADetalle?.Invoke(this, tema);
innerPanel.Children.Add(btnIr);
```
