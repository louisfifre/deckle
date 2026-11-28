//! The C ABI exercised the way the managed side calls it, with local rules only.
use deckle_adblock::*;

fn engine(rules: &str) -> *mut adblock::Engine {
    unsafe { adblock_engine_new(rules.as_ptr(), rules.len()) }
}

fn block(e: *const adblock::Engine, url: &str, src: &str, ty: &str) -> u8 {
    unsafe { adblock_should_block(e, url.as_ptr(), url.len(), src.as_ptr(), src.len(), ty.as_ptr(), ty.len()) }
}

fn cosmetic(e: *const adblock::Engine, url: &str) -> String {
    unsafe {
        let mut n = 0usize;
        let p = adblock_cosmetic(e, url.as_ptr(), url.len(), &mut n);
        assert!(!p.is_null());
        let s = std::str::from_utf8(std::slice::from_raw_parts(p, n)).unwrap().to_string();
        adblock_string_free(p, n);
        s
    }
}

#[test]
fn blocks_what_a_rule_names_and_only_that() {
    let e = engine("||ads.browser.test^\n@@||ads.browser.test/ok.js^\n");
    assert_eq!(block(e, "https://ads.browser.test/ad.png", "https://browser.test/", "image"), 1);
    assert_eq!(block(e, "https://ads.browser.test/ok.js", "https://browser.test/", "script"), 0);
    assert_eq!(block(e, "https://browser.test/app.js", "https://browser.test/", "script"), 0);
    unsafe { adblock_engine_free(e) };
}

#[test]
fn cosmetic_selectors_come_back() {
    let e = engine("browser.test##.ad-banner\n");
    assert!(cosmetic(e, "https://browser.test/page").contains(".ad-banner"));
    assert!(!cosmetic(e, "https://other.test/page").contains(".ad-banner"));
    unsafe { adblock_engine_free(e) };
}

#[test]
fn a_scriptlet_rule_yields_an_injected_script() {
    let e = engine("browser.test##+js(tiny-marker, hello)\n");
    // "(function(){ window.__marker = '{{1}}'; })();" in base64
    let body = "window.__marker = '{{1}}';";
    let b64 = base64(body.as_bytes());
    let json = format!(r#"[{{"name":"tiny-marker.js","aliases":[],"kind":"template","content":"{b64}"}}]"#);
    assert_eq!(unsafe { adblock_use_resources(e, json.as_ptr(), json.len()) }, 1);
    let s = cosmetic(e, "https://browser.test/page");
    assert!(s.contains("__marker"), "{s}");
    assert!(s.contains("hello"), "{s}");
    unsafe { adblock_engine_free(e) };
}

fn base64(b: &[u8]) -> String {
    const T: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    let mut o = String::new();
    for c in b.chunks(3) {
        let n = (c[0] as u32) << 16 | (*c.get(1).unwrap_or(&0) as u32) << 8 | *c.get(2).unwrap_or(&0) as u32;
        o.push(T[(n >> 18) as usize & 63] as char);
        o.push(T[(n >> 12) as usize & 63] as char);
        o.push(if c.len() > 1 { T[(n >> 6) as usize & 63] as char } else { '=' });
        o.push(if c.len() > 2 { T[n as usize & 63] as char } else { '=' });
    }
    o
}

#[test]
fn bad_input_does_not_crash() {
    let bad = [0xffu8, 0xfe, 0xfd];
    unsafe {
        assert!(adblock_engine_new(bad.as_ptr(), bad.len()).is_null());
        let e = engine("||ads.browser.test^\n");
        assert_eq!(adblock_should_block(e, bad.as_ptr(), 3, std::ptr::null(), 0, std::ptr::null(), 0), 0);
        assert_eq!(block(e, "not a url", "", "script"), 0);
        let _ = block(e, "https://ads.browser.test/x", "https://browser.test/", "no-such-type");
        assert_eq!(adblock_use_resources(e, bad.as_ptr(), 3), 0);
        let junk = "{ not json";
        assert_eq!(adblock_use_resources(e, junk.as_ptr(), junk.len()), 0);
        assert_eq!(block(std::ptr::null(), "https://ads.browser.test/x", "", "image"), 0);
        let mut n = 7usize;
        assert!(adblock_cosmetic(std::ptr::null(), bad.as_ptr(), 3, &mut n).is_null());
        assert_eq!(n, 0);
        adblock_engine_free(std::ptr::null_mut());
        adblock_engine_free(e);
    }
}
