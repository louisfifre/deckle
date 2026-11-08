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
}
