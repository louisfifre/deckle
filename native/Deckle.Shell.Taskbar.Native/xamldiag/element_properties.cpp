#include "pch.h"
#include "element_properties.h"

namespace wf = winrt::Windows::Foundation;
namespace wux = winrt::Windows::UI::Xaml;
namespace wuxc = winrt::Windows::UI::Xaml::Controls;
namespace wuxcp = winrt::Windows::UI::Xaml::Controls::Primitives;
namespace wum = winrt::Windows::UI::Xaml::Media;
namespace wuxs = winrt::Windows::UI::Xaml::Shapes;

namespace
{
    std::wstring Number(double value)
    {
        if (value != value) return L"auto";   // NaN is how XAML spells an unset Width or Height
        wchar_t buffer[48];
        swprintf_s(buffer, L"%g", value);
        return buffer;
    }

    std::wstring ColorText(winrt::Windows::UI::Color const& c)
    {
        wchar_t buffer[16];
        swprintf_s(buffer, L"#%02X%02X%02X%02X", c.A, c.R, c.G, c.B);
        return buffer;
    }

    std::wstring BrushText(wum::Brush const& brush)
    {
        if (!brush) return L"null";
        if (auto solid = brush.try_as<wum::SolidColorBrush>())
        {
            std::wstring text = ColorText(solid.Color());
            if (brush.Opacity() != 1.0) text += L"@" + Number(brush.Opacity());
            return text;
        }
        return std::wstring(winrt::get_class_name(brush));
    }

    std::wstring ThicknessText(wux::Thickness const& t)
    {
        return Number(t.Left) + L"," + Number(t.Top) + L"," + Number(t.Right) + L"," + Number(t.Bottom);
    }

    // The text of a value returned by ReadLocalValue; empty when the property
    // has no local value.
    std::wstring LocalValueText(wf::IInspectable const& value)
    {
        if (!value) return L"null";
        if (value == wux::DependencyProperty::UnsetValue()) return {};

        if (auto property = value.try_as<wf::IPropertyValue>())
        {
            switch (property.Type())
            {
            case wf::PropertyType::Double: return Number(property.GetDouble());
            case wf::PropertyType::Single: return Number(property.GetSingle());
            case wf::PropertyType::Int32: return std::to_wstring(property.GetInt32());
            case wf::PropertyType::UInt32: return std::to_wstring(property.GetUInt32());
            case wf::PropertyType::Boolean: return property.GetBoolean() ? L"true" : L"false";
            case wf::PropertyType::String: return std::wstring(property.GetString());
            default: break;
            }
        }
        // Enumerations are boxed as IReference<enum>, which has no PropertyType of its own.
        if (auto alignment = value.try_as<wf::IReference<wux::TextAlignment>>()) return L"TextAlignment." + std::to_wstring(static_cast<int>(alignment.Value()));
        if (auto horizontal = value.try_as<wf::IReference<wux::HorizontalAlignment>>()) return L"HorizontalAlignment." + std::to_wstring(static_cast<int>(horizontal.Value()));
        if (auto vertical = value.try_as<wf::IReference<wux::VerticalAlignment>>()) return L"VerticalAlignment." + std::to_wstring(static_cast<int>(vertical.Value()));
        if (auto visibility = value.try_as<wf::IReference<wux::Visibility>>()) return L"Visibility." + std::to_wstring(static_cast<int>(visibility.Value()));
        if (auto weight = value.try_as<wf::IReference<winrt::Windows::UI::Text::FontWeight>>()) return L"FontWeight." + std::to_wstring(weight.Value().Weight);
        if (auto thickness = value.try_as<wf::IReference<wux::Thickness>>()) return ThicknessText(thickness.Value());
        if (auto corner = value.try_as<wf::IReference<wux::CornerRadius>>())
        {
            auto c = corner.Value();
            return Number(c.TopLeft) + L"," + Number(c.TopRight) + L"," + Number(c.BottomRight) + L"," + Number(c.BottomLeft);
        }
        if (auto color = value.try_as<wf::IReference<winrt::Windows::UI::Color>>()) return ColorText(color.Value());
        if (auto brush = value.try_as<wum::Brush>()) return BrushText(brush);
        return std::wstring(winrt::get_class_name(value));
    }

    class Writer
    {
    public:
        void Add(std::wstring_view key, std::wstring_view value)
        {
            if (!m_text.empty()) m_text += L';';
            m_text += key;
            m_text += L'=';
            m_text += value;
        }

        // Adds `key` with the local value of `property`, when it has one.
        void Local(wux::DependencyObject const& object, wux::DependencyProperty const& property, std::wstring_view key)
        {
            try
            {
                std::wstring text = LocalValueText(object.ReadLocalValue(property));
                if (!text.empty()) Add(key, text);
            }
            catch (...) { Add(key, L"?"); }
        }

        template <typename Read>
        void Effective(std::wstring_view key, Read&& read)
        {
            try { Add(key, read()); }
            catch (...) { Add(key, L"?"); }
        }

        std::wstring Take() { return std::move(m_text); }

    private:
        std::wstring m_text;
    };

    void DescribeVisualStates(Writer& out, wux::FrameworkElement const& element)
    {
        try
        {
            auto groups = wux::VisualStateManager::GetVisualStateGroups(element);
            if (!groups || groups.Size() == 0) return;

            std::wstring text;
            for (auto const& group : groups)
            {
                if (!text.empty()) text += L'|';
                text += group.Name();
                auto current = group.CurrentState();
                text += L'[';
                if (current) text += current.Name();
                text += L']';
            }
            out.Add(L"~vsg", text);
        }
        catch (...) { out.Add(L"~vsg", L"?"); }
    }
}

namespace deckle::xamldiag
{
    std::wstring DescribeValue(wf::IInspectable const& value)
    {
        return LocalValueText(value);
    }

    std::wstring DescribeElement(wf::IInspectable const& instance)
    {
        Writer out;

        auto element = instance.try_as<wux::FrameworkElement>();
        if (!element) return {};

        out.Local(element, wux::FrameworkElement::WidthProperty(), L"Width");
        out.Local(element, wux::FrameworkElement::HeightProperty(), L"Height");
        out.Local(element, wux::FrameworkElement::MinWidthProperty(), L"MinWidth");
        out.Local(element, wux::FrameworkElement::MinHeightProperty(), L"MinHeight");
        out.Local(element, wux::FrameworkElement::MaxWidthProperty(), L"MaxWidth");
        out.Local(element, wux::FrameworkElement::MaxHeightProperty(), L"MaxHeight");
        out.Local(element, wux::FrameworkElement::MarginProperty(), L"Margin");

        out.Effective(L"~actual", [&] { return Number(element.ActualWidth()) + L"x" + Number(element.ActualHeight()); });
        out.Effective(L"~halign", [&] { return std::to_wstring(static_cast<int>(element.HorizontalAlignment())); });   // 0 Left, 1 Center, 2 Right, 3 Stretch
        if (element.Visibility() != wux::Visibility::Visible) out.Add(L"~visibility", L"Collapsed");
        if (element.Opacity() != 1.0) out.Add(L"~opacity", Number(element.Opacity()));

        if (auto shape = instance.try_as<wuxs::Shape>())
        {
            out.Effective(L"Fill", [&] { return BrushText(shape.Fill()); });
            out.Effective(L"Stroke", [&] { return BrushText(shape.Stroke()); });
            out.Local(shape, wuxs::Shape::StrokeThicknessProperty(), L"StrokeThickness");
        }

        if (auto text = instance.try_as<wuxc::TextBlock>())
        {
            out.Effective(L"~Text", [&]
            {
                std::wstring value(text.Text());
                if (value.size() > 48) value.resize(48);
                return value;
            });
            out.Effective(L"~FontSize", [&] { return Number(text.FontSize()); });
            out.Local(text, wuxc::TextBlock::ForegroundProperty(), L"Foreground");
            out.Local(text, wuxc::TextBlock::TextAlignmentProperty(), L"TextAlignment");
            out.Effective(L"~TextAlign", [&] { return std::to_wstring(static_cast<int>(text.TextAlignment())); });   // 0 Center, 1 Left, 2 Start, 3 Right, 4 End, 5 Justify
            out.Local(text, wuxc::TextBlock::FontWeightProperty(), L"FontWeight");
        }

        if (auto control = instance.try_as<wuxc::Control>())
        {
            out.Local(control, wuxc::Control::PaddingProperty(), L"Padding");
            out.Local(control, wuxc::Control::BackgroundProperty(), L"Background");
            out.Local(control, wuxc::Control::ForegroundProperty(), L"Foreground");
            out.Local(control, wuxc::Control::FontSizeProperty(), L"FontSize");
        }

        if (instance.try_as<wuxc::Border>())
        {
            out.Local(element, wuxc::Border::BackgroundProperty(), L"Background");
            out.Local(element, wuxc::Border::BorderBrushProperty(), L"BorderBrush");
            out.Local(element, wuxc::Border::PaddingProperty(), L"Padding");
            out.Local(element, wuxc::Border::CornerRadiusProperty(), L"CornerRadius");
        }

        if (instance.try_as<wuxc::Panel>())
        {
            out.Local(element, wuxc::Panel::BackgroundProperty(), L"Background");
        }

        if (instance.try_as<wuxc::ContentPresenter>())
        {
            out.Local(element, wuxc::ContentPresenter::PaddingProperty(), L"Padding");
        }

        if (auto grid = instance.try_as<wuxc::Grid>())
        {
            out.Effective(L"~grid", [&]
            {
                return std::to_wstring(grid.RowDefinitions().Size()) + L"r" +
                       std::to_wstring(grid.ColumnDefinitions().Size()) + L"c";
            });
        }

        if (auto stack = instance.try_as<wuxc::StackPanel>())
        {
            out.Effective(L"~Orientation", [&] { return stack.Orientation() == wuxc::Orientation::Horizontal ? L"Horizontal" : L"Vertical"; });
            out.Local(element, wuxc::StackPanel::SpacingProperty(), L"Spacing");
        }

        if (auto wrap = instance.try_as<wuxc::WrapGrid>())
        {
            out.Effective(L"~MaximumRowsOrColumns", [&] { return std::to_wstring(wrap.MaximumRowsOrColumns()); });
            out.Effective(L"~ItemWidth", [&] { return Number(wrap.ItemWidth()); });
            out.Effective(L"~ItemHeight", [&] { return Number(wrap.ItemHeight()); });
            out.Effective(L"~Orientation", [&] { return wrap.Orientation() == wuxc::Orientation::Horizontal ? L"Horizontal" : L"Vertical"; });
        }

        DescribeVisualStates(out, element);
        return out.Take();
    }
}

