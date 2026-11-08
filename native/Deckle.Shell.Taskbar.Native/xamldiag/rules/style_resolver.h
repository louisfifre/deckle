#pragma once

namespace deckle::rules
{
    struct ResolvedSetter
    {
        winrt::Windows::UI::Xaml::DependencyProperty property{ nullptr };
        winrt::Windows::Foundation::IInspectable value{ nullptr };
    };

    // Turns "Fill" + "Transparent" on a Windows.UI.Xaml.Shapes.Rectangle into the
    // DependencyProperty and the value object, by letting the XAML parser read a
    // throw-away <Style><Setter/></Style>. The parser knows every property and
    // every converter, so the engine carries no table of property types.
    //
    // `type` is the runtime class name the style targets; when the parser cannot
    // resolve it (some of Explorer's types have no XAML type), the setter is read
    // against FrameworkElement, which is enough for the common properties.
    // Throws winrt::hresult_error when the property or the value cannot be read.
    //
    // XAML parsing needs a UI thread, and a Style and its values belong to the
    // thread that made them: call it on the thread of the element and keep the
    // result there.
    //
    // Adapted from Windhawk's windows-11-taskbar-styler mod (GetStyleFromXamlSetters),
    // GPL-3.0. See NOTICE.md.
    ResolvedSetter ResolveSetter(std::wstring_view type, std::wstring_view property, std::wstring_view value);

    // Whether an element other than the first one needs a new value object: a
    // Brush can be shared by many elements, a Transform or a Style cannot.
    bool IsShareable(winrt::Windows::Foundation::IInspectable const& value);
}
