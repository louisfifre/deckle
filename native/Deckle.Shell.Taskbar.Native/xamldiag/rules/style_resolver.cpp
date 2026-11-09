#include "pch.h"
#include "style_resolver.h"

// Adapted from Windhawk's windows-11-taskbar-styler mod (GetStyleFromXamlSetters,
// GetStyleFromXamlSettersWithFallbackType), GPL-3.0. See NOTICE.md.

namespace
{
    namespace wf = winrt::Windows::Foundation;
    namespace wux = winrt::Windows::UI::Xaml;

    std::wstring EscapeAttribute(std::wstring_view text)
    {
        std::wstring result;
        for (wchar_t c : text)
        {
            switch (c)
            {
            case L'&': result += L"&amp;"; break;
            case L'<': result += L"&lt;"; break;
            case L'>': result += L"&gt;"; break;
            case L'"': result += L"&quot;"; break;
            default: result += c; break;
            }
        }
        return result;
    }

    wux::Style StyleFromSetter(std::wstring_view type, std::wstring_view property, std::wstring_view value)
    {
        std::wstring xaml =
            LR"(<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml")";

        // A type with a namespace ("Taskbar.TaskbarFrame") is written as a
        // prefixed name over a using: namespace; a bare one is a XAML type.
        if (auto dot = type.rfind(L'.'); dot != std::wstring_view::npos)
        {
            xaml += L"\n    xmlns:target=\"using:" + EscapeAttribute(type.substr(0, dot)) + L"\">\n";
            xaml += L"    <Style TargetType=\"target:" + EscapeAttribute(type.substr(dot + 1)) + L"\">\n";
        }
        else
        {
            xaml += L">\n    <Style TargetType=\"" + EscapeAttribute(type) + L"\">\n";
        }

        xaml += L"        <Setter Property=\"" + EscapeAttribute(property) + L"\"";
        if (!value.empty() && value.front() == L'<')
        {
            xaml += L">\n            <Setter.Value>\n" + std::wstring(value) + L"\n            </Setter.Value>\n        </Setter>\n";
        }
        else
        {
            xaml += L" Value=\"" + EscapeAttribute(value) + L"\" />\n";
        }
        xaml += L"    </Style>\n</ResourceDictionary>";

        auto dictionary = wux::Markup::XamlReader::Load(xaml).as<wux::ResourceDictionary>();
        return dictionary.First().Current().Value().as<wux::Style>();
    }
}

namespace deckle::rules
{
    ResolvedSetter ResolveSetter(std::wstring_view type, std::wstring_view property, std::wstring_view value)
    {
        wux::Style style{ nullptr };
        try
        {
            style = StyleFromSetter(type, property, value);
        }
        catch (winrt::hresult_error const& error)
        {
            // "Failed to create a System.Type from the text": the class has no
            // XAML type. Read the setter against FrameworkElement instead.
            constexpr winrt::hresult typeNotResolved{ static_cast<int32_t>(0x802B000A) };
            constexpr std::wstring_view fallback = L"Windows.UI.Xaml.FrameworkElement";
            if (error.code() != typeNotResolved || type == fallback) throw;
            style = StyleFromSetter(fallback, property, value);
        }

        auto setter = style.Setters().GetAt(0).as<wux::Setter>();
        wf::IInspectable result = setter.Value();

        // A FontWeight read by the parser comes back as its number (Medium is
        // 500), and a FontWeight property takes a FontWeight, not an Int32:
        // SetValue answers E_NOINTERFACE. Box the struct.
        if (property == L"FontWeight" || property.ends_with(L".FontWeight"))
        {
            if (auto number = result.try_as<wf::IReference<int32_t>>())
                result = winrt::box_value(winrt::Windows::UI::Text::FontWeight{ static_cast<uint16_t>(number.Value()) });
        }
        return { setter.Property(), result };
    }

    bool IsShareable(wf::IInspectable const& value)
    {
        if (!value) return true;
        if (value.try_as<winrt::Windows::UI::Xaml::Media::Brush>()) return true;
        return !value.try_as<wux::DependencyObject>();
    }
}
