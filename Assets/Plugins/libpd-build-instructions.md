# Building libpd native plugins for TopoSynthGame

All platforms must ship the **same libpd: Pd 0.55.2, built with multiple-instance
support** (`-DPDINSTANCE -DPDTHREADS`). `LibPdInstance.cs` now uses the
**per-instance** libpd model (Pd 0.54+): it creates one `libpd_new_instance()`
per component and keeps a separate queued message ring buffer + hooks per
instance. An older/global-queued libpd (e.g. the Pd-0.49/0.50 builds this project
used to ship) is **not** compatible and will misbehave or crash with more than one
instance.

> If you only swap some platforms, the ones left on old libpd will break when two
> `UnirackInstance`s (e.g. `ur` + `ur2`) run at once. Update them all.

## Status

| Platform | File | Status |
|---|---|---|
| Android arm64 | `Assets/Plugins/Android/arm64-v8a/libpd.so` | Done — Pd 0.55.2, multi |
| Linux x64 (editor) | `Assets/Plugins/x64/libpd.so` | Done — Pd 0.55.2, multi |
| Windows x64 | `Assets/Plugins/x64/libpd.dll` | **TODO** (currently Pd 0.49) |
| Windows x86 | `Assets/Plugins/x86/libpd.dll` | **TODO** |
| macOS | `Assets/Plugins/libpd.bundle` | **TODO** (needs a Mac) |
| iOS | `Assets/Plugins/libpd-ios-multi.a` | **TODO** (needs a Mac) |

## 0. Get the source (same on every machine)

Pin the exact commit that pairs libpd with Pure Data 0.55.2:

```bash
git clone --recursive https://github.com/libpd/libpd.git
cd libpd
git checkout 51b2cdc            # "updated to pd 0.55-2"
git submodule update --init --recursive
# sanity check: should print MINOR 55 / BUGFIX 2
grep -E "#define PD_(MINOR|BUGFIX)_VERSION" pure-data/src/m_pd.h
```

The key build flag everywhere is **`MULTI=true`** (this adds `-DPDINSTANCE
-DPDTHREADS`). `UTIL=true` (default) is required for the queued ring buffer.

## 1. Linux x64 (already done — for reference)

```bash
make clean
make MULTI=true
# output: libs/libpd.so  ->  Assets/Plugins/x64/libpd.so
```

## 2. Windows x64 and x86

Build natively with **MSYS2** (easiest) or cross-compile from Linux with MinGW-w64.

### MSYS2 (native, recommended)

Install [MSYS2](https://www.msys2.org/). The 64- and 32-bit DLLs are built from
two different shells.

**x64** — open the *"MSYS2 MINGW64"* shell:

```bash
pacman -S --needed mingw-w64-x86_64-gcc make git
cd /path/to/libpd
make clean
make MULTI=true
# output: libs/libpd.dll  ->  Assets/Plugins/x64/libpd.dll
```

**x86** — open the *"MSYS2 MINGW32"* shell:

```bash
pacman -S --needed mingw-w64-i686-gcc make git
cd /path/to/libpd
make clean
make MULTI=true
# output: libs/libpd.dll  ->  Assets/Plugins/x86/libpd.dll
```

### Runtime DLL dependencies (important)

The MinGW build depends on `libwinpthread-1.dll` (that's the only non-system
dependency the current shipped DLL has). Check what your DLL actually needs:

```bash
objdump -p libs/libpd.dll | grep "DLL Name"
```

Ship every non-system DLL listed (ignore `KERNEL32.dll`, `msvcrt.dll`,
`WS2_32.dll`, etc.). Copy them next to `libpd.dll`:

- x64: `mingw64/bin/libwinpthread-1.dll` -> `Assets/Plugins/x64/`
- x86: `mingw32/bin/libwinpthread-1.dll` -> `Assets/Plugins/x86/`

(The repo also has `libgcc_s_seh-1.dll` in `x64/`; the current DLL doesn't import
it, so it's optional. Keep whatever `objdump` reports.)

To avoid shipping any runtime DLLs instead, link them statically:

```bash
make MULTI=true LDFLAGS="-static -static-libgcc"
```

### Cross-compiling Windows from Linux (optional)

```bash
sudo apt install gcc-mingw-w64-x86-64 gcc-mingw-w64-i686
make clean && make MULTI=true CC=x86_64-w64-mingw32-gcc \
  CFLAGS="-DPDINSTANCE -DPDTHREADS" SOLIB_EXT=dll \
  SOLIB_LDFLAGS="-shared -Wl,--export-all-symbols"
```

Native MSYS2 is more reliable than cross-compiling; prefer it if you can.

## 3. macOS `.bundle` (needs a Mac with Xcode)

Use the multi-instance Xcode target shipped in the repo:

```bash
cd /path/to/libpd            # already checked out at 51b2cdc
xcodebuild -project libpd.xcodeproj -target libpd-osx-multi -configuration Release
```

This produces a macOS dynamic library/bundle built with multi-instance support.
Place/rename it as `Assets/Plugins/libpd.bundle` (replace the existing one, keep
the `.bundle.meta`).

If you prefer the Makefile: `make MULTI=true` produces `libs/libpd.dylib`; Unity
loads it as a macOS plugin once placed and named to match the existing
`libpd.bundle` import settings.

## 4. iOS `.a` (needs a Mac with Xcode)

The existing file `libpd-ios-multi.a` is produced by the `libpd-ios-multi` target,
which already enables multiple-instance support:

```bash
cd /path/to/libpd            # already checked out at 51b2cdc
xcodebuild -project libpd.xcodeproj -target libpd-ios-multi -configuration Release
# output: libpd-ios-multi.a  ->  Assets/Plugins/libpd-ios-multi.a
```

On iOS, `LibPdInstance.cs` links it statically (`DLL_NAME = "__Internal"`), so the
`.a` must contain the multi-instance build. The Xcode target tracks the
`pure-data` submodule, so with `51b2cdc` checked out it will be Pd 0.55.2.

## 5. Verify every binary

After building each one, confirm version + multi-instance before committing:

```bash
# Pd version (expect: Pd-0.55.2)
strings <binary> | grep -E "^Pd-0\.[0-9]" | head -1

# per-instance queued support (expect a match)
#   Linux/Android: nm -D libpd.so | grep libpd_set_instancedata
#   Windows:       nm libpd.dll   | grep libpd_set_instancedata   (or objdump -x)
#   macOS/iOS:     nm -gU <file>  | grep libpd_set_instancedata
```

`libpd_set_instancedata` only exists in the per-instance (Pd 0.54+) queued layer,
so its presence is the quick "is this the right build" check.

## 6. After replacing binaries

- **Restart the Unity editor** — it does not hot-reload native plugins.
- Test with two `UnirackInstance`s (`ur` + `ur2`), each with a different patch and
  preset, and confirm both produce independent audio.

## Notes

- Build environment used for the Linux/Android binaries: libpd commit `51b2cdc`
  (`git describe` = `0.14.1-23-g51b2cdc`), Pure Data 0.55.2.
- If you later see intermittent crashes under heavy load with multiple instances,
  the next step is wrapping the libpd calls in `libpd_lock()` / `libpd_unlock()`
  (the modern libs export them); ask before adding, since it touches the audio
  path.
