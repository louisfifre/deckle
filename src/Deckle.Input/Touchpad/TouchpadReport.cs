namespace Deckle.Input;

// One HID input report decoded by the parser — the transport unit, not
// the consumer unit (see CONTEXT.md § Input: a report may be a fragment
// of a contact frame). ContactCount carries the device's frame-total
// declaration when this report opens a frame, and 0 when it continues
// one (the hybrid-mode rule); Contacts carries every slot present in the
// report layout, valid slots first — the assembler decides how many to
// keep based on the declared count.
public sealed record TouchpadReport(
    uint ScanTime,
    int ContactCount,
    bool ButtonDown,
    TouchpadContact[] Contacts);
