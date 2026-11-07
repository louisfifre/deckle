# portaudio.dll

Built by `scripts/setup/build-portaudio.ps1`; rerun it rather than editing this folder by hand.

- Source: https://github.com/PortAudio/portaudio.git
- Commit: 1ca0eeedb678c3cca80f3306c474d69a7a4b5278
- Options: -DPA_USE_WASAPI=ON -DPA_USE_ASIO=OFF -DPA_USE_WMME=OFF -DPA_USE_DS=OFF -DPA_USE_WDMKS=OFF -DPA_USE_WDMKS_DEVICE_INFO=OFF -DPA_BUILD_SHARED_LIBS=ON -DPA_DLL_LINK_WITH_STATIC_RUNTIME=ON -DPA_BUILD_TESTS=OFF -DPA_BUILD_EXAMPLES=OFF -DCMAKE_C_FLAGS=/DPA_WASAPI_MAX_CONST_DEVICE_COUNT=32
- SHA256: 27520c78885b1ef0b56625d78e60eee1c8032ce93ba41bed9be556f8e1b87d5a

The license PortAudio asks to keep with every copy is `portaudio.LICENSE.txt`, beside the DLL.
