# AGENTS.md

This file provides guidance to AI coding agents when working with code in this repository.

## Code Conventions

### General
- Nullable reference types are enabled (`#nullable enable`) everywhere.
- Unsafe blocks are allowed globally.
- To pin a managed array or buffer, prefer the `fixed` statement for scope-bound pinning; reach for `GCHandle.Alloc(…, GCHandleType.Pinned)` only when the pin must outlive the current scope.
- To read fields of a native interop struct from a raw byte buffer, mark the method `unsafe` and access the struct in place through a `fixed` pointer (`fixed (byte* p = buffer) { var header = (SomeHeader*)p; … header->field … }`) rather than copying the whole struct out (`*(SomeHeader*)p`) or `MemoryMarshal.Read<T>()`; use `sizeof(SomeHeader)` for the length check and offsets rather than a hardcoded byte count. If the method also needs to be awaitable, keep it non-`async` and return the `Task` directly (an `await` cannot sit in the `unsafe` context, and a pointer cannot be held across it).
- Compare native handles against `IntPtr.Zero` explicitly (`handle == IntPtr.Zero`), not `default`.
- All public async methods return `Task`/`ValueTask`; UI-thread operations use `Dispatcher.UIThread`.
- `[ThreadSafe]` attributes mark thread-safe members explicitly.

### File and Type Organization
- One type per file; file name matches the type name exactly. **A class which exists only to hold extension members for another type is the exception.** When the extended type `T` is defined in this project, its `TExtensions` class is **preferred** to live at the bottom of `T.cs`, below `T` itself, rather than in a file of its own — as `BitmapFormatExtensions` does in `BitmapFormat.cs`, `ImageRendererExtensions` in `IImageRenderer.cs`, and `ColorTableExtensions` in `ColorTable.cs`. An extension class for a type this project does not define (a framework or library type) has no such file to join, so it keeps its own. `BayerPatternExtensions.cs` predates the rule and has not been moved yet.
- Namespace matches the folder path: `Carina.PixelViewer.<Subfolder>`.
- Inner types within a class/file are ordered **alphabetically** by name.
- `extension` blocks (C# 14 extension members) are placed **first** in the containing class, before all other members; they are not sorted with the members listed below. Members inside an `extension` block are ordered alphabetically.
- Enum values are ordered **alphabetically** — except in native-interop declarations (e.g. the P/Invoke types in `Native/Win32.cs`), where an enum mirrors the OS definition and keeps its native member names, ordering, and value grouping (for a native constant set used as a single field's value, such as `BITMAPV5HEADER.bV5CSType`, prefer a `[uint]`-backed enum over flattened constants).

Members inside a class are ordered as follows:

1. **Public constants** — no section comment; each member has its own `///` XML doc.
2. **Public static fields** — no section comment; each member has its own `///` XML doc.
3. **Inner types** — alphabetically ordered; each type has its own dedicated comment.
4. **Constants** (private/internal) — under a `// Constants.` section comment.
5. **Static fields** (private/internal) — under a `// Static fields.` section comment.
6. **Private fields** — under a `// Fields.` section comment.
7. **Static initializer** — under a `// Static initializer.` section comment.
8. **Constructors** — under a `// Constructor(s).` section comment.
9. **Non-private fields, properties, and methods** — ordered **alphabetically** by member name. Each member is preceded by:
   - a `///` XML doc comment for public members, OR
   - a single-line `//` comment describing the member, for private/internal members.

Exception: struct fields declared with `[StructLayout(LayoutKind.Sequential)]` must preserve their memory-layout order and cannot be reordered alphabetically.

### Naming

| Element | Convention | Example |
|---|---|---|
| Public properties | PascalCase | `ImageWidth`, `IsActivated` |
| Private fields (instance and static) | camelCase; instance fields always qualified with `this.` | `this.imageSource`, `this.isRendering`, `defaultRenderingOptions` |
| Internal / protected / public fields | PascalCase | `DefaultColorSpace` |
| Platform-specific fields/members | Prefix with the platform (`MacOS` / `Windows` / `Linux`) | `MacOSDataForTypeSelector`, `MacOSNSPasteboardClass` |
| Static `ObservableProperty` fields | PascalCase + `Prop` suffix | `ColorSpaceProp`, `BrightnessAdjustmentProp` |
| Private/helper methods | PascalCase | `GenerateHistogramImage()`, `UpdateSourceImageEffectiveBits()` |
| Public methods | PascalCase | `Render()`, `ExportImage()` |
| Async methods | Must end with `Async` | `RenderAsync()` |
| Constants | PascalCase | `RenderImageDelay` |
| Parameters & local variables | camelCase | `cancellationToken`, `pixelStride` |
| Interfaces | `I` prefix + PascalCase | `IImageDataSource`, `IImageRenderer` |
| Event handlers | `On` prefix | `OnRendererPropertyChanged` |

**All methods use PascalCase regardless of accessibility**, per the standard .NET naming convention — a private or internal helper method is named exactly like a public one (`GenerateHistogramImage()`, not `generateHistogramImage()`). camelCase is reserved for private fields, parameters, and local variables.

**Field casing follows accessibility, not lifetime** — a field is camelCase when it is `private` (instance or static alike) and PascalCase when it is `internal`, `protected`, or `public`. When adding a field to an existing type, follow the convention already used in that type rather than mixing two casings within one file.

### Formatting & Structure
- **File-scoped namespaces** — use `namespace Foo.Bar;` (not block-scoped).
- **`using` directives outside** the namespace declaration. Always import the correct namespace when using a new type; remove unused `using` directives after modifying code.
- **Allman-style braces** — opening brace on its own line for types and methods; single-statement bodies may omit braces, but only when that single statement fits on one line. If the inner statement spans multiple lines (e.g. the outer of a stacked `using` whose inner `using` has a multi-line block), the outer statement must use braces.
- **A type with no body is declared on a single line, ending with a semicolon** rather than an empty `{ }` — `class JpegCompoundMediaMetadata(TiffMediaMetadata? exif, XmpMediaMetadata? xmp) : CompoundMediaMetadata(exif, xmp);`. This is the usual shape of a type whose primary constructor does all the work. It applies to the **type** only: an empty constructor or method body keeps its braces, as `BaseFileFormatParser`'s constructor does.
- **`try`/`catch`/`finally` blocks** always use full braces even when the body is a single statement or empty.
- **`this.` prefix** on all instance member accesses (fields and properties). It does **not** apply to primary-constructor parameters, which are accessed directly by name.
- **Static members are accessed through the type that declares them**, never through a derived type that merely inherits them. `CurrentOrNull` is declared on `CarinaStudio.Application`, which `App` inherits it from (`App` → `AppSuiteApplication` → `CarinaStudio.Application`), so write `Application.CurrentOrNull` — **not** `App.CurrentOrNull`. Both spellings bind to the identical member, so this is purely about showing the reader where the member actually lives, and about not implying that the derived type adds something it does not. `using CarinaStudio;` resolves the bare `Application` name; qualify it in a file that also imports `Avalonia`, where `Avalonia.Application` would collide.
- **Primary constructors** preferred over explicit constructors when the body would only assign fields.
- **Expression-bodied members** for concise single-expression properties and methods.
- **Assignments are dedicated statements** — never combine an assignment with a value read in the same expression. Do not consume the result of an assignment (`=`, `??=`, `++`, `--`, etc.) as a sub-expression (method argument, condition, initializer, return value, expression-bodied member, etc.). Assign on its own line first, then read the variable/field on the following line. Write `this.pngFormat ??= DataFormat.CreateBytesPlatformFormat("image/png");` then `await clipboard.SetValueAsync(this.pngFormat, data);` — **not** `await clipboard.SetValueAsync(this.pngFormat ??= DataFormat.CreateBytesPlatformFormat("image/png"), data);`. (Lazy-cache fields follow the same pattern as `MacOSTiffPasteboardType ??= …;` on its own line before the field is used.) This also rules out returning an assignment: a lazily-initialized property must use a block getter that assigns on one line and returns on the next (`get { field ??= …; return field; }`), **not** an expression body that consumes the assignment (`=> field ??= …`).
- **Enum members** are listed consecutively with no blank line between them, even when each carries an XML doc comment.
- **Blank lines between members** — two blank lines between members of a top-level type; one blank line between members of an inner (nested) type.

### Fields & Properties

- Access instance fields with `this.` consistently — never omit it.
- Mark thread-shared fields `volatile`; use `Interlocked.*` for atomic updates.
- Use `[ThreadSafe]` / `[UsedOnBackgroundThread]` / `[CalledOnBackgroundThread]` attributes to document thread semantics.
- Register reactive properties with `ObservableProperty.Register<TOwner, T>(nameof(...))` rather than implementing `INotifyPropertyChanged` manually. Coercion and validation logic go in the registration call (`coerce:` / `validate:`).
- `ObservableProperty<T>` fields are named `XxxProp` (e.g. `ColorSpaceProp`), **never** `XxxProperty`, regardless of visibility. Reason: Avalonia 12 compiled bindings resolve any public static field named `<Member>Property` on the bound type as an `AvaloniaProperty` and fail compilation when it is not one; the `Prop` suffix is applied to all visibilities for consistency. `AvaloniaProperty` fields on controls keep the standard `XxxProperty` suffix — that convention is required by Avalonia and does not conflict. The existing `XxxProperty`-suffixed `ObservableProperty` fields are renamed as part of the Avalonia 12 upgrade; use `Prop` in all new code.
- **Omit explicit generic type arguments** when the compiler infers them from the arguments. For example `ISettings.SetValue()` (on `Settings` / `PersistentState`) infers from the `SettingKey<T>`: write `this.PersistentState.SetValue(SomeKey, value)`, not `this.PersistentState.SetValue<T>(SomeKey, value)`.
- **Time units** — milliseconds are the default. Bare `Timeout` / `Delay` / `Interval` names (e.g. `RenderImageDelay = 500`) are always milliseconds; do not append `Ms`. Use a unit suffix only when the value is **not** in milliseconds (`SomethingSeconds`, `SomethingMicroseconds`, `SomethingTicks`).
- When a property needs custom accessor logic (validation, change notification, etc.) but `ObservableProperty.Register` is not in use, prefer the C# `field` keyword over a manually-declared backing field. Example:
  ```csharp
  public int Count
  {
      get;
      private set
      {
          if (field == value)
              return;
          field = value;
          this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
      }
  }
  ```

### Types & Nullability
- Nullable reference types are fully enabled — annotate everything.
- Prefer **`var`** for local variables when the type is inferable from the right-hand side; fall back to an explicit type only when the inferred type would be unclear to a reader of the line in isolation (e.g. when the right-hand side is a method call whose return type isn't evident from its name).
- **An initializer of `null` or `default` infers nothing**, so a local declared with one takes its explicit type: `string? offsetString = null;`, **not** `var offsetString = (string?)null;` or `var colorSpace = default(ColorSpace);`. The cast in those forms exists only to give `var` something to bind to, so it says the same thing as the explicit type while reading as if a conversion were happening. Several older files still use the cast form (`Session.cs`, `SkiaFileFormatParser.cs`, `TiffBasedFileFormatParser.cs`), so the surrounding code is not the guide here — do not mirror it into new code, and do not sweep the existing occurrences either. When the local is definitely assigned by every branch that follows, drop the initializer entirely (`string? offsetString;`) rather than seeding it with a `null` nothing reads.
- Use **`is not null` / `is null`** pattern matching instead of `!= null` / `== null` in all new code — this holds inside compound boolean guards (`… && x is not null`) and when null-checking an `out` variable from a `Try…` method (e.g. `TryGetEntryData(out var data) && data is not null`), not just standalone `if` conditions. Reserve `==` / `!=` for non-null comparisons such as reference or value equality (e.g. `fileFormat == FileFormats.Png`).
- Use **null-conditional** (`?.`) and **null-coalescing** (`??`) operators for safe access and defaults.
- Use `.AsNonNull()` (framework extension) to assert non-null instead of `!`.
- Use `.Let(it => ...)` for safe chained operations on nullable values.
- Use `.Also(it => ...)` for fluent object initialization.
- Null-coalesce events: `this.SomeEvent?.Invoke(this, e)`.
- **Never pass `default` as an argument** — always use an explicit value (e.g. `CancellationToken.None`, `TimeSpan.Zero`).

### Async
- Business logic async methods return `Task` / `Task<T>` — never `async void` (UI event handlers are the sole exception).
- Pass `CancellationToken` through the full call chain. Never swallow `OperationCanceledException`.
- To check for cancellation, call `token.ThrowIfCancellationRequested()` rather than manually testing `token.IsCancellationRequested` and throwing `TaskCanceledException` / `OperationCanceledException`. This applies to **all new code**, including the cancellation check that immediately follows an `await Task.Run(...)` block — write `token.ThrowIfCancellationRequested();` there, even though some existing parsers still use the older manual `if (token.IsCancellationRequested) throw new TaskCanceledException();` form (do not mirror that legacy pattern into new code).
- When calling an async method, **always use the overload that accepts a `CancellationToken`** if one exists. Pass the available token if you have one; otherwise pass `CancellationToken.None` explicitly. Examples: `Task.Run(work, token)`, `task.WaitAsync(timeout, CancellationToken.None)`, `stream.ReadAsync(buffer, token)`.
- In event-handler lambdas, name the sender parameter (`(sender, e) => ...`) instead of discarding it (`(_, e) => ...`) when the body fire-and-forgets an async call (`_ = SomeAsync(...)`). The `_ = ...` pattern silently discards the returned `Task` (and any exception it would surface); keeping `sender` named flags the handler as stateful for the reader and gives a debugger something to inspect when the fire-and-forget faults.

### Collections
- `ImmutableList<T>` / `ImmutableHashSet<T>` for snapshot/read-only data.
- `ObservableList<T>` for mutable collections that the UI binds to; expose them as `ReadOnlyObservableList<T>` publicly.
- Prefer the `.IsEmpty()` / `.IsNotEmpty()` / `.IsNullOrEmpty()` extension methods over `.Count == 0` / `.Count > 0` / `.Length == 0` / `.Length > 0`, and over emptiness pattern matching (e.g. `x is null or []`, `x is { Count: > 0 }`, `x is null || x.Count == 0`). They cover every `ICollection<T>` / `IReadOnlyCollection<T>` — lists, sets, dictionaries, queues, stacks, and arrays alike — so the same three names apply whatever the concrete collection type is. Reserve `.Count` / `.Length` comparisons for non-zero thresholds (e.g. `Count >= maxCount`).
- The nullable-accepting overloads (`IsNotEmpty` / `IsNullOrEmpty`) are annotated with `[NotNullWhen]`, so a successful check propagates non-null state to a later dereference — no `.AsNonNull()` assertion is needed after the guard. If an overload is ever found not to propagate it, that is a bug to fix in AppBase, not a reason to avoid these methods or to work around them at the call site.
- Prefer C# **collection expressions** over `new T[]{ ... }`, `new List<T>{ ... }`, or `Array.Empty<T>()`, whenever no constructor arguments (e.g. initial capacity, custom comparer) are required. They target arrays, spans, and interfaces such as `IList<T>` / `IReadOnlyList<T>`, so use them for collection-typed return values, fields, and locals too — including untargeted contexts such as `object`-typed parameters, where C# 14's natural type (`List<T>`) applies. Pad the brackets with a space around non-empty contents — `[ a, b, c ]`, `[ new(8, 4, width * 4) ]`, `[ ..source ]` — but keep an empty expression as `[]` (e.g. `IList<ImagePlaneOptions> CreateDefaultPlaneOptions(...) => [ new(8, 4, width * 4) ];`).

### Patterns

- **Early returns** for guard clauses (dispose checks, cancellation, already-done checks) at the top of methods.
- **`switch` expressions** for multi-way type or value dispatch.
- **Unused lambda parameters** use the ignored identifier `_` (e.g. `(_, e) => ...`, `async _ => ...`) instead of a named parameter the body never reads. Exception: keep the sender parameter named in an event-handler lambda whose body fire-and-forgets an async call (see the Async section). Caveat: a single `_` parameter is a real identifier, so a body containing `out _` / `_ = ...` discards will conflict (CS1503 or silent capture) — restructure the body to avoid the discard rather than naming the parameter.
- **`.Let()` / `.Also()` / `.Use()`** for functional-style chaining and initialization:
  ```csharp
  var set = new HashSet<string>().Also(s =>
  {
      s.Add("foo");
      this.extras?.Let(s.AddAll);
  });
  ```
- **`.Setup()` for `IDisposable` initialization** — when creating an `IDisposable` and setting its properties immediately, do not use object-initializer syntax (`new Foo { Prop = value }`): if the initializer throws, the instance is never disposed. Use the `.Setup(it => ...)` extension instead, which guarantees `Dispose()` is called when the setup action throws:
  ```csharp
  using var encoder = new PngImageEncoder().Setup(it =>
  {
      it.Quality = 90;
  });
  ```
- **Extension members (C# 14)** — when extending a type, prefer an extension property inside an `extension(T value)` block over a `GetX()`-style extension method, whenever the accessor is a pure, side-effect-free projection that reads naturally as a property. Use it so call sites read `format.ChannelCount` instead of `format.GetChannelCount()`:
  ```csharp
  static class ImageFormatExtensions
  {
      extension(ImageFormat format)
      {
          /// <summary>
          /// Get number of channels of the format.
          /// </summary>
          public int ChannelCount => format switch
          {
              ImageFormat.Luminance or ImageFormat.Bayer => 1,
              ImageFormat.RGB or ImageFormat.YUV => 3,
              ImageFormat.ARGB => 4,
              _ => throw new NotImplementedException(),
          };
      }
  }
  ```

### Comments
- XML doc (`/// <summary>`) on all public types and members.
- `<summary>` always uses the three-line form — opening tag, body, closing tag — even when the body is a single sentence:
  ```csharp
  /// <summary>
  /// Short sentence describing the member.
  /// </summary>
  ```
- Other XML doc tags (`<remarks>`, `<param>`, `<returns>`, etc.) collapse to a single line when their content fits on one — open tag, body, and close tag all together:
  ```csharp
  /// <param name="value">The value to set.</param>
  /// <returns>True if the operation succeeded.</returns>
  ```
- If a longer explanation is genuinely needed (subtle invariants, cross-cutting behavior, etc.), put it in a separate `<remarks>` tag — do not pad the `<summary>`.
- For a member that overrides a base member or implements an interface/abstract member (including explicit interface implementations), use `/// <inheritdoc/>` (self-closing) rather than a `//` comment or a restated `<summary>`. Members that are not overrides/implementations — private helpers, constructors — keep the usual `//` comment or `<summary>`.
- Inline section comments inside method bodies are **lowercase**, no trailing period, on their own line before the code: `// start rendering`, `// update histogram`
- Inside **any** code block — method body, `case` block, `if`/`else`, `for`/`while`/`foreach`, `try`/`catch`/`finally`, lambda body — group related statements into logical blocks separated by a single blank line, and give each block its own one-line comment. This includes both the **leading** and the **trailing** block: a final `return new { … }` separated from the preceding code by a blank line still needs its own comment. The leading block's comment sits directly under the opening `{` with no preceding blank line; later blocks are preceded by a single blank line. Exception: when an enclosing block contains only a single logical block, no comment is required.
- When splitting an existing logical block into two or more blocks (during a refactor or edit), audit the result: every resulting block — including the leading one — must carry its own comment. Never leave the leading block uncommented while later blocks are commented, and never add sub-blocks without comments because the original block had none under the single-block exception.
- Comments above private/internal members use sentence case with a trailing period: `// Called when renderer property changed.`
- No end-of-line comments.

### XAML / Avalonia
- **Compiled vs. reflection bindings — an Avalonia 12 rule.** This repo is still on Avalonia 11 (`AvaloniaVersion` in `Directory.Build.props`), where compiled bindings are opt-in; the rule below takes effect with the Avalonia 12 upgrade and does not govern today's XAML. On Avalonia 12: prefer compiled bindings as much as possible. `x:CompileBindings="True"` becomes redundant (compiled is the default) and is omitted. `x:CompileBindings="False"` is **not allowed** — it switches compiled bindings off for an entire scope, taking every binding in that scope down with the one that needed it; when a reflection binding genuinely is needed, opt that **single** binding out with the dedicated `{ReflectionBinding …}` markup extension instead. Carina Studio's AppSuiteBase framework is already on Avalonia 12 and states this as an active rule — see the `AGENTS.md` of the [AppSuiteBase repository](https://github.com/carina-studio/AppSuiteBase).
- Use `{DynamicResource Brush/...}` for theme-sensitive values; `{StaticResource Double/...}` for fixed values.
- String resources via markup extension: `{asXaml:StringResource SessionView.SomeKey}`.
- Namespace aliases follow the pattern `xmlns:asXaml="using:CarinaStudio.AppSuite.Xaml"`, `xmlns:asControls="using:CarinaStudio.AppSuite.Controls"`, `xmlns:app="using:Carina.PixelViewer"`.
- Resource names use slash-separated paths: `Brush/SessionView.StatusBar.Background`.
- To combine multiple bindings with boolean AND / OR, prefer the `{asXaml:AndBindings …}` / `{asXaml:OrBindings …}` markup extensions (comma-separated child `{Binding …}` entries) over a `MultiBinding` with `{x:Static aConverters:BoolConverters.And}` / `.Or`. The markup-extension form is more concise and is the established pattern in this repo.
- **`asControls:DialogItem` sizing** — use the default item size only when the item contains a **ComboBox** input or has a **description** (`asControls:DialogElement.TextRole="DescriptionBelowLabel"`); for every other item (plain TextBox/IntegerTextBox, ToggleSwitch, etc. with just a label) set `ItemSize="Small"`.

### Logging

- **Log levels** —
  - `Trace`: high-frequency per-event detail that would flood production logs (e.g. per-frame or per-pixel-block progress). `Debug` is visible in production builds, so anything that can fire faster than once per rendering pass / session should be `Trace`, not `Debug`.
  - `Debug`: bounded-frequency diagnostic events (per-file open, per-render lifecycle, format-detection outcome).
  - `Information`: subsystem lifecycle and operator-visible state transitions.
  - `Warning`: unusual but non-fatal situations (unsupported format, malformed header, fallback taken).
  - `Error`: unexpected exceptions or operations that genuinely failed.
- **Message text** —
  - Use **sentence case** — capitalize the first word (`"Rendering: source file has no ICC profile"`, not `"rendering: source file …"`).
  - For dispatch / request outcomes, use the format **`Subject: {target} [<outcome>]`** — the bracketed token is a machine-friendly result code. Examples:
    - `Render: NV12 [ok]`
    - `Open: /path/to/file [unsupported_format]`
    - `Encode: PNG [internal_error]`
  - Use **lowercase** result codes inside the brackets (`[ok]`, `[unsupported_format]`, `[internal_error]`).
  - When a message carries one or more named state values, list them as `name: {value}` separated by commas, after the descriptive prefix or outcome bracket. The **name** is written as plain English words separated by spaces, not as the C# identifier — `effective bits: {bits}`, not `effective_bits: {bits}` or `effectiveBits: {bits}`. The placeholder inside `{...}` is the structured-logging key and follows normal C# naming (camelCase). Examples:
    - `Render: BGRA [ok], width: {width}, height: {height}`
    - `Open: file format mismatch, detected: {detected}, requested: {requested}`
- **Logger names** — for classes with per-instance identifiers (sessions, renderers, data sources), construct a logger named `<TypeName>-<Id>` via `app.LoggerFactory.CreateLogger($"{nameof(MyType)}-{this.Id}")`. The id then appears in NLog's `${logger:shortName=true}` prefix, so log messages don't need to embed it.

---

## Localization & String Resources

UI strings live in `PixelViewer/Strings/`:
- `Default.xaml` — base English (en-US). All keys live here.
- `zh-TW.xaml`, `zh-CN.xaml` — Traditional / Simplified Chinese. Only contain entries that differ from `Default.xaml`; the resource system falls back to the default for missing keys.
- `*-OSX.xaml`, `*-Linux.xaml` — platform overrides for keystrokes (`⌘` vs `Ctrl`) and OS-specific labels (Finder / File Manager / File Explorer). Only override what's platform-specific.

### English style

- **Title case** for strings without trailing period (item titles, option labels, button text). AP-style: lowercase short prepositions/conjunctions (`a/an/the/of/for/in/on/at/to/by/per/as/and/or/if/after/when/during`); capitalize 4+ letter prepositions when at the start (`Before`, `Between`, `With`) and all verbs including `Is`/`Are`.
- **Sentence case** for strings ending with `.` or `?` (descriptions, error/status messages, hints).
- Articles in titles: keep when natural (`Show CPU/Memory Usage on the Status Bar`, `Hibernated to Save Resources`).
- Avoid `Max` in long option labels — prefer `Maximum`.

### Quoting

Quoting follows the target locale, not the source text. Mirrored from AppSuiteBase's `AGENTS.md`, so change it here only when it changes there:

| Locale | Quoting a name in prose |
|---|---|
| `zh-TW` | `AAA「BBB」CCC` — corner brackets, no surrounding whitespace |
| `zh-CN` | `AAA “BBB” CCC` — curly double quotes, one half-width space on each side |
| `Default` (en) | `AAA 'BBB' CCC` |

- Corner brackets (`「」`) are **wrong in `zh-CN`**, which uses `“”`.
- Drop the surrounding space when the quote sits next to `，`, `。`, `、` or `…` — the full-width punctuation already carries it (`…已成功导出至 “BBB”。`, `正在执行 “{0}”…`).
- **File names and file paths take ASCII single quotes in every locale**: `AAA 'Path' BBB`, spaced the same way. This overrides the locale quoting above, so a placeholder holding a path is never wrapped in `「」` or `“”`. An *alias* for a location is a name, not a path, and keeps the locale quotes.
- A product or application name substituted from a placeholder takes no quotes at all: `无法启用 {0}，请尝试再次启用。`
- Document titles use `《…》` in `zh-CN` / `zh-TW`.
- The rule is about the **language**, not the file type, so it governs `PixelViewer/Resources/UserAgreement*.md` and `PrivacyPolicy*.md` as well as the string resources — a reader of the zh-CN User Agreement sees the Taiwan brackets just as plainly. The `"AS IS"` of the English disclaimer is the exception which proves it: those ASCII double quotes are a warranty term of art rather than a name being quoted, so they stay.

### Chinese conventions

- `zh-TW.xaml` uses Taiwan terms: 檔案, 資訊, 資料, 介面, 影像, etc.
- `zh-CN.xaml` uses Mainland terms: 文件, 信息, 数据, 界面, 图像, etc. **Watch for Taiwan-leaning leftovers** — common ones to convert:
  - General vocabulary: 资讯→信息, 资料→数据 (when meaning "data"), 介面→界面, 回应→响应, 网路→网络, 数位→数字, 灰阶→灰度, 套用→应用, 储存→保存, 开启→打开 (when meaning "open file/dialog"; `开启` is fine for "enable"), 取得→获取, 透过→通过, 效能→性能, 载入→加载, 设定→设置 (as a UI label), 选取→选择/选中, 撷取→抓取/截取, 拖曳→拖动, 影像→图像, 字型→字体, 色彩→颜色, 辨识→识别, 追踪→跟踪, 预设→默认.
  - Compound terms: 资料库→数据库, 资料来源→数据源, 资料点→数据点, 资料夹→文件夹, 剪贴簿→剪贴板, 状态列→状态栏, 文件总管→Windows 资源管理器.
- **Watch for Traditional characters mixed inside Simplified strings** — e.g. `設定` (Traditional 設) inside an otherwise-Simplified string, or a description that switches script mid-sentence. These are silent bugs that pass spell-check.
- For "PNG" the `G` is *Graphics* — translate as 图形 / 圖形, not 图像 / 影像.
- For "double-tap", zh-TW uses 點兩下 (**not** 雙擊); zh-CN uses 双击.
- For "frame", zh-TW uses 畫格 (**not** 畫面 or 影格); zh-CN uses 帧.
- zh-TW phrasing preferences: prefer 不包含 over 不具備, and 而不是 over 而非. zh-CN keeps 不具备 / 而非.
- For "keyboard shortcut", zh-TW uses 快速鍵 (the standard Taiwan vendor term, per Microsoft / Apple Taiwan), **not** 快捷鍵 (the Mainland-origin form); zh-CN uses 快捷键.
- Tab UI term: 标签页 (Mainland) / 分頁 (Taiwan).
- "Image plane" translates as 圖層 (zh-TW) / 图层 (zh-CN) — the established product term (see 圖層參數/图层参数 in the options dialog); never the literal 影像平面 / 图像平面.
- For "set" (a value), zh-TW uses 設定, zh-CN uses 设置. A description explaining a special value uses the declarative pattern 「設定為 X 表示…」 (zh-TW) / “设置为 X 表示…” (zh-CN, per § *Quoting*) — state what the value means, not a conditional 「若…則設為 X」.
- "Packed into bits": 緊密位元排列 (zh-TW) / 紧密位排列 (zh-CN — 位, not 位元).
- For English entries phrased as "Added support for X" (typical in `ChangeList*.md` and similar notes), translate as `支援 X` (zh-TW) / `支持 X` (zh-CN), not the literal `新增 X 的支援 / 新增 X 的支持`. That covers a new **capability** ("support for opening TIFF images"). When what was added is instead a new **concrete item the user picks from a list** — an image format, a color space — use `新增` plus the item as a noun phrase: `` 新增 `BGRA_8888` 影像格式。 `` (zh-TW) / `` 新增 `BGRA_8888` 图像格式。 `` (zh-CN). This is not the banned form above, which is `新增` wrapped around `的支援 / 的支持`.
- "Demosaicing" translates as 去馬賽克 (zh-TW) / 去马赛克 (zh-CN), and the term **is itself a verb** — never prefix it with 進行 / 进行. Write 使用雙線性演算法去馬賽克 (zh-TW) / 使用双线性算法去马赛克 (zh-CN), not 使用雙線性演算法進行去馬賽克. Note 演算法 (zh-TW) vs 算法 (zh-CN).
- For "fix" wording, **zh-CN uses 修复 everywhere, including the section header** (`修复…的问题`, the `## 错误修复` header, `其他错误修复`); **zh-TW keeps 修正** (`修正…的問題`, `## 錯誤修正`).
- Description strings end with the full-width period 。 — no trailing space before `</sys:String>`.

### Keys

- Keys are stable identifiers and must match exactly across all language files. A typo like `…ImageHint` vs `…Image` causes silent fallback to English.

### Retrieving strings in code

- To get a string resource as an `IObservable` (e.g. for `MessageDialog.Message` or bindings set in code), prefer `Application.GetObservableString("Some.Key")` over `Control.GetResourceObservable("String/Some.Key")`. Note `GetObservableString` takes the **bare** key (`"MainWindow.SomeKey"`) — the `String/` prefix is added internally.

---

## Change Lists

`PixelViewer/ChangeList.md`, `PixelViewer/ChangeList-zh-TW.md`, and `PixelViewer/ChangeList-zh-CN.md` describe the changes shipping in the next version.

- **Key names and key combinations must be wrapped in single-backtick inline code** — e.g. `` `⌘Q` ``, `` `Ctrl+Q` ``, `` `⌘←` ``, `` `Ctrl+Shift+F` ``. Use single backticks, not triple. This rule applies in every locale variant; the inline code wrapping is identical across English, zh-TW, and zh-CN.
- **When an entry lists a shortcut for more than one platform, order the platforms Windows/Linux first, then macOS** — e.g. `Ctrl+V` on Windows/Linux, `⌘V` on macOS.
- English entries use past tense (`Added`, `Improved`, `Prevented`, `Fixed`).
- Each new bullet must be mirrored in all three locale files; do not update one without updating the others.
- **Place a new bullet by importance and feature grouping** — the more significant features come first within a section, and bullets covering the same feature area sit together (the two frame entries, the two TIFF entries). A new bullet is therefore neither appended to the end nor prepended to the top by default: find the entry it belongs beside, and place it there. The existing order can look chronological because the newer features have happened to be the bigger ones — do not read it as a rule.
- **Keep the existing order of entries** — placing a new bullet must not resort or regroup the entries already present in a section.

---

## Code Review Checklist

### Correctness
- Logic is correct for all paths, including edge cases (empty collections, null values, zero counts).
- Multi-step operations that must be atomic are protected by a lock or semaphore across all steps, not just individual operations.
- State mutations under a lock do not leak mutable references that can be read or written outside the lock.
- `async`/`await` is used correctly — no fire-and-forget unless intentional; no `.Result` or `.Wait()` blocking on async code.
- `CancellationToken` is propagated through all async calls; `OperationCanceledException` is not swallowed.
- `IDisposable` resources are disposed in all paths, including error paths.

### Thread Safety
- Shared mutable fields accessed from multiple threads are protected consistently.
- No TOCTOU (time-of-check/time-of-use) races — check and act happen under the same lock or synchronization primitive.
- Background-thread methods are marked `[CalledOnBackgroundThread]`; UI-thread calls are dispatched via `SynchronizationContext` or guarded with `CheckAccess()`.

### Error Handling
- Exceptions are not silently swallowed — at minimum log the error.
- Expected failure paths (file missing, unsupported format) are logged at `Warning`; unexpected exceptions at `Error`.
- Best-effort operations (e.g. cleanup) catch and log per-item rather than aborting the entire operation.

### Style
- All coding style rules above are followed (naming, formatting, nullability, patterns).
- Unused `using` directives removed; correct namespaces imported for any new types introduced.
- Static members are accessed through their declaring type, not through a derived type (`Application.CurrentOrNull`, not `App.CurrentOrNull`).
- `default` is not passed as an argument — explicit values used instead.
- Inline section comments (inside methods) are lowercase with no trailing period; each logical block has its own comment preceded by a blank line, including the leading and trailing blocks whenever the body has been split into two or more blocks (single-block bodies need no comment); member-level comments use sentence case with a trailing period.
- **Member ordering** is correct: public constants → public static fields → inner types → constants → static fields → private fields → static initializer → constructor(s) → all remaining members sorted alphabetically by name. Verify after adding, renaming, or moving any member.
