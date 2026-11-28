//! deckle_adblock: the ad blocker's native engine for Deckle Browser.
//!
//! A thin C ABI over Brave's `adblock` crate (adblock-rust, MPL-2.0, Copyright Brave
//! Software), which parses Adblock Plus / uBlock Origin filter lists and answers two
//! questions: should this request be blocked, and what must be hidden or injected on
//! this page. The managed side is `src/Deckle.Browser.Adblock` (`NativeAdblock`).
//!
//! Contract of the C ABI:
//! - Every string comes in as UTF-8 bytes, a pointer and a length in bytes. There is no
//!   terminator. A null pointer with a length of 0 is the empty string; bytes that are
//!   not valid UTF-8 make the call fail softly (null, 0 or "do not block"), never panic.
//! - `adblock_engine_new` returns an engine the caller owns and releases once with
//!   `adblock_engine_free`. A null engine is accepted everywhere and means "no blocking".
//! - `adblock_cosmetic` returns a buffer allocated here (JSON of the page's hide
//!   selectors and injected script) and its length in `out_len`; the caller releases it
//!   with `adblock_string_free(ptr, len)`, with that same length, and never otherwise.
//! - The engine is not `Sync` (it holds a boxed resource storage that is not): the caller
//!   serialises every call on one engine, reads included. The managed wrapper does it
//!   with one lock. Two different engines are independent.
//! - Release profile: panic = abort, so a bug aborts the process instead of unwinding
//!   across the C boundary; the inputs above are checked so that one is not expected.

use adblock::lists::{FilterSet, ParseOptions};
use adblock::request::Request;
use adblock::resources::Resource;
use adblock::Engine;

unsafe fn text<'a>(p: *const u8, len: usize) -> Option<&'a str> {
    if p.is_null() {
        return if len == 0 { Some("") } else { None };
    }
    std::str::from_utf8(unsafe { std::slice::from_raw_parts(p, len) }).ok()
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn adblock_engine_new(rules_ptr: *const u8, rules_len: usize) -> *mut Engine {
    let Some(rules) = (unsafe { text(rules_ptr, rules_len) }) else {
        return std::ptr::null_mut();
    };
    let mut set = FilterSet::new(false);
    set.add_filter_list(rules.to_string(), ParseOptions::default());
    Box::into_raw(Box::new(Engine::new_with_filter_set(set)))
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn adblock_engine_free(engine: *mut Engine) {
    if !engine.is_null() {
        drop(unsafe { Box::from_raw(engine) });
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn adblock_should_block(
    engine: *const Engine,
    url_ptr: *const u8,
    url_len: usize,
    src_ptr: *const u8,
    src_len: usize,
    type_ptr: *const u8,
    type_len: usize,
) -> u8 {
    if engine.is_null() {
        return 0;
    }
    let (Some(url), Some(src), Some(ty)) = (unsafe {
        (text(url_ptr, url_len), text(src_ptr, src_len), text(type_ptr, type_len))
    }) else {
        return 0;
    };
    match Request::new(url, src, ty, "GET") {
        Ok(req) => unsafe { &*engine }.check_network_request(&req).should_block() as u8,
        Err(_) => 0,
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn adblock_use_resources(engine: *mut Engine, json_ptr: *const u8, len: usize) -> u8 {
    if engine.is_null() {
        return 0;
    }
    let Some(json) = (unsafe { text(json_ptr, len) }) else { return 0 };
    match serde_json::from_str::<Vec<Resource>>(json) {
        Ok(res) => {
            unsafe { &mut *engine }.use_resources(res);
            1
        }
        Err(_) => 0,
    }
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn adblock_cosmetic(
    engine: *const Engine,
    url_ptr: *const u8,
    url_len: usize,
    out_len: *mut usize,
) -> *mut u8 {
    if !out_len.is_null() {
        unsafe { *out_len = 0 };
    }
    if engine.is_null() || out_len.is_null() {
        return std::ptr::null_mut();
    }
    let Some(url) = (unsafe { text(url_ptr, url_len) }) else { return std::ptr::null_mut() };
    let res = unsafe { &*engine }.url_cosmetic_resources(url);
    let Ok(s) = serde_json::to_string(&res) else { return std::ptr::null_mut() };
    let b: Box<[u8]> = s.into_bytes().into_boxed_slice();
    unsafe { *out_len = b.len() };
    Box::into_raw(b) as *mut u8
}

#[unsafe(no_mangle)]
pub unsafe extern "C" fn adblock_string_free(ptr: *mut u8, len: usize) {
    if !ptr.is_null() {
        drop(unsafe { Box::from_raw(std::ptr::slice_from_raw_parts_mut(ptr, len)) });
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn smoke() {
        let rules = "||ads.example.com^\nexample.com##.ad-banner\n";
        unsafe {
            let e = adblock_engine_new(rules.as_ptr(), rules.len());
            assert!(!e.is_null());
            let b = |u: &str, s: &str, t: &str| {
                adblock_should_block(e, u.as_ptr(), u.len(), s.as_ptr(), s.len(), t.as_ptr(), t.len())
            };
            assert_eq!(b("https://ads.example.com/x.js", "https://example.com/", "script"), 1);
            assert_eq!(b("https://example.com/app.js", "https://example.com/", "script"), 0);
            assert_eq!(b("not a url", "", "script"), 0);
            let u = "https://example.com/";
            let mut n = 0usize;
            let p = adblock_cosmetic(e, u.as_ptr(), u.len(), &mut n);
            let s = std::str::from_utf8(std::slice::from_raw_parts(p, n)).unwrap().to_string();
            assert!(s.contains(".ad-banner"), "{s}");
            adblock_string_free(p, n);
            let j = "[]";
            assert_eq!(adblock_use_resources(e, j.as_ptr(), j.len()), 1);
            adblock_engine_free(e);
        }
    }
}
