# Vintage Story GUI Implementation Guide

## 1. Basic Structure and Inheritance
- **For Block Entity GUIs**: Inherit from `GuiDialogBlockEntity`
- **For HUD Elements**: Inherit from `HudElement`
- **For General Purpose GUIs**: Inherit from `GuiDialog`

## 2. Dialog Construction Pattern
1. **Define Bounds**:
   - Use `ElementBounds` for positioning and sizing
   - Common patterns: `ElementBounds.Fixed()`, `ElementStdBounds.AutosizedMainDialog`
   - Set alignment with `.WithAlignment(EnumDialogArea.X)`

2. **Create Composer**:
   ```csharp
   var composer = capi.Gui.CreateCompo("uniqueDialogId", dialogBounds);
   ```

3. **Add Background** (if needed):
   ```csharp
   composer.AddShadedDialogBG(bgBounds);
   // or
   composer.AddDialogBG(bgBounds);
   ```

4. **Add Title Bar** (if needed):
   ```csharp
   composer.AddDialogTitleBar("Title Text", OnCloseButtonClicked);
   ```

5. **Add Content Elements**:
   - Text: `AddStaticText()`, `AddDynamicText()`
   - Controls: `AddButton()`, `AddDropDown()`, etc.
   - Layout: Use proper bounds and nesting

6. **Compose and Store**:
   ```csharp
   Composers["dialogId"] = composer.Compose();
   ```

7. **Open Dialog**:
   ```csharp
   TryOpen();
   ```

## 3. Element Bounds Best Practices
- **Dialog Bounds**: Define the overall container
- **Content Bounds**: Define each element's position and size
- **Background Bounds**: Usually `ElementBounds.Fill.WithFixedPadding()`
- **Nesting**: Use `.WithChildren()` to establish parent-child relationships
- **Positioning**: Use `.BelowCopy()`, `.RightCopy()`, etc. for relative positioning

## 4. Text Elements
- **Static Text**: `AddStaticText()` for unchanging text
- **Dynamic Text**: `AddDynamicText()` for text that changes
- **Fonts**: Use `CairoFont` factory methods like `WhiteDetailText()`, `WhiteSmallText()`

## 5. Backgrounds and Styling
- **Dialog Background**: `AddShadedDialogBG()` or `AddDialogBG()`
- **Insets**: `AddInset()` for darkened sections with borders
- **Custom Drawing**: `AddStaticCustomDraw()` for custom rendering

## 6. Scrolling Content
- Use `BeginClip()` / `EndClip()` to define the visible area
- Add a scrollbar with `AddVerticalScrollbar()`
- Set heights with `scrollbar.SetHeights(visibleHeight, totalHeight)`
- Update content position in the scrollbar callback

## 7. Event Handling
- Define callbacks for buttons, scrollbars, etc.
- Use `TryClose()` to properly close dialogs

## References
- [Official Vintage Story GUI Documentation](https://wiki.vintagestory.at/Modding:GUIs)
- [ElementStdBounds Source](https://github.com/anegostudios/vsapi/blob/master/Client/UI/ElementStdBounds.cs)
