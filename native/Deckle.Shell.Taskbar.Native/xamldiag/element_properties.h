#pragma once

namespace deckle::xamldiag
{
    // One line describing a XAML element for the log: `key=value;key=value`.
    // Keys without a mark are the element's local values (what a style or code
    // set directly), keys starting with `~` are effective values. Reads only,
    // and only on the element's UI thread: the visual tree callback runs there.
    //
    // Every property is read on its own, so one that throws costs one key.
    std::wstring DescribeElement(winrt::Windows::Foundation::IInspectable const& element);

    // The text of one property value, as DescribeElement writes it: a number,
    // a thickness, a colour or a brush spelt out, any other object by its class
    // name. Empty for DependencyProperty::UnsetValue. Two values with the same
    // text are the same value for the rule engine (see property_override.cpp).
    std::wstring DescribeValue(winrt::Windows::Foundation::IInspectable const& value);
}
