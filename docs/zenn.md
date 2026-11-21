---
title: "モニターの明るさを時刻で変える Windows アプリを作った（DDC/CI と HDR の SDR 明るさ）"
emoji: "🌗"
type: "tech"
topics: ["csharp", "windows", "winforms", "ddcci", "個人開発"]
published: false
---

<!--
  投稿メモ（このコメントは表示されません）
  ・画像 3 枚（docs/images/main.png, edit.png, correction.png）をエディタにドラッグ＆ドロップしてアップロードし、
    下の ![...](images/...) をアップロード後の URL に差し替えてください（tools/make-screenshots.ps1 で生成）。
  ・準備ができたら上の published を true にして公開します（Web エディタなら「公開する」ボタン）。
  ・Web エディタでは上の title / emoji / topics は使われないので、「公開設定」で
    絵文字 🌗、トピック（csharp, windows, winforms, ddcci, 個人開発）、カテゴリー Tech を設定してください。
  ・太字の閉じの ** の直前が「）」などの記号で、直後が文字だと、太字にならず ** がそのまま表示されます。
-->

夜になると、昼間はちょうどよかったモニターがまぶしく感じる。かといって毎晩モニターのボタンで暗くするのは面倒……ということで、**決めた時刻にディスプレイの明るさを自動で変える** Windows アプリ「輝度スケジューラ」を作りました。

![メイン画面。朝 80%、昼 100%、夜 22 時に向けて 30% までなめらかに下げる設定](images/main.png)

以前作った「[壁紙スケジューラ](https://zenn.dev/walt_owen/articles/3291d8d844a05d)」の明るさ版で、同じく Windows 標準の `csc.exe` だけでビルドしています。この記事では、外付けモニターの明るさを変える **DDC/CI**、そして HDR がオンのときにそれが効かない問題をどう解決したかを中心に書きます。

## できること

- 時刻ごとに明るさ（0〜100%）を登録し、毎日自動で変える
- 「その時刻に切り替え」と「なめらかに変化」を選べる
- 外付けモニター（DDC/CI）とノート PC の内蔵画面に対応
- **HDR がオンのモニター**は Windows の「SDR コンテンツの明るさ」を変える
- 2 台のモニターで見え方が違うときの**モニターごとの補正**

## ダウンロード

@[card](https://woy74237-ship-it.github.io/brightness-scheduler/)

インストール不要で、ZIP を展開して exe を起動するだけで使えます（Windows 10 / 11）。ソースコードは MIT ライセンスで公開しています。

@[card](https://github.com/woy74237-ship-it/brightness-scheduler)

---

ここからは作り方の話です。

## 外付けモニターの明るさは DDC/CI で変える

ノート PC の内蔵画面なら WMI の `WmiMonitorBrightnessMethods` で明るさを変えられますが、デスクトップの外付けモニターには使えません（`WmiMonitorBrightness` を問い合わせても「サポートされていません」になります）。

外付けモニターは **DDC/CI**（ディスプレイケーブル経由でモニターと通信する規格）で操作します。Windows には `dxva2.dll` にそのための API があり、明るさは **VCP コード 0x10** です。

```csharp
[DllImport("dxva2.dll")]
static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
[DllImport("dxva2.dll", CharSet = CharSet.Unicode)]
static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
[DllImport("dxva2.dll")]
static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte code, IntPtr type, out uint current, out uint max);
[DllImport("dxva2.dll")]
static extern bool SetVCPFeature(IntPtr hMonitor, byte code, uint value);
[DllImport("dxva2.dll")]
static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);
```

`EnumDisplayMonitors` で得た `HMONITOR` から物理モニターのハンドルを取り、最大値を読んでから、割合に応じた値を書き込みます。

```csharp
uint cur, max;
if (GetVCPFeatureAndVCPFeatureReply(m.Handle, 0x10, IntPtr.Zero, out cur, out max) && max > 0)
{
    uint value = (uint)Math.Round(max * percent / 100.0);
    if (cur != value) SetVCPFeature(m.Handle, 0x10, value);
}
```

実際に使ってみて気をつけた点です。

- **1 回の通信に数十ミリ秒かかる**ので、UI スレッドでは呼ばず専用のスレッドで処理しています。処理中に新しい値が来たら古い値は捨て、最新の値だけを書き込みます。
- **ときどき失敗する**ので、少し待って 3 回まで試します。
- モニターの設定は本体の記憶領域に書き込まれるので、**値が変わったときだけ**書き込みます。15 秒ごとにスケジュールを確認しますが、なめらかに変える場合も 1% 変わったときだけ通信します。

### 「汎用 PnP モニター」の本当の名前を出す

`PHYSICAL_MONITOR.szPhysicalMonitorDescription` には「Generic PnP Monitor」としか入らないモニターがあります。画面に「対象: Generic PnP Monitor」と出ても、どれのことかわかりません。

モニターの EDID に書かれたモデル名は WMI の `WmiMonitorID`（`root\WMI`）の `UserFriendlyName` で取れるので、`HMONITOR` と突き合わせて名前を出しています。

```
HMONITOR
  → GetMonitorInfo で \\.\DISPLAY1
  → EnumDisplayDevices(\\.\DISPLAY1, 0, EDD_GET_DEVICE_INTERFACE_NAME)
       DeviceID = \\?\DISPLAY#ABC1234#5&...&UID256#{e6f07b5f-...}
  → DISPLAY\ABC1234\5&...&UID256 に変換
  → WmiMonitorID の InstanceName（DISPLAY\ABC1234\5&...&UID256_0）の先頭と一致するものを探す
```

## HDR がオンだと、DDC/CI で書き込んでも明るさが変わらない

ここが一番ハマったところです。HDR をオンにしているモニターで、`SetVCPFeature` は **成功を返し、読み返しても値は変わっている**のに、画面の明るさがまったく変わりませんでした。

HDR がオンのあいだは、多くのモニターが本体の明るさ設定を無視します。このときデスクトップの明るさを決めているのは、Windows の「設定 → ディスプレイ → HDR → **SDR コンテンツの明るさ**」です。そこで、HDR がオンのモニターではこちらを変えることにしました。

### HDR がオンかどうか

`DisplayConfigGetDeviceInfo` の `DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO`（9）で、モニターごとに HDR（Advanced Color）が有効かどうかがわかります。

```csharp
var color = new ADVANCED_COLOR { h = Header<ADVANCED_COLOR>(9, target.adapterId, target.id) };
DisplayConfigGetDeviceInfo(ref color);
bool hdr = (color.value & 2) != 0;   // advancedColorEnabled
```

### SDR コンテンツの明るさの読み書き

読み取りは公開されている `DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL`（11）です。値は **1000 = 80 nits** の単位で返ってきます。

書き込みには公開された API がないのですが、Windows の設定画面が使っている**非公開のタイプ `0xFFFFFFEE`**（-18）を `DisplayConfigSetDeviceInfo` に渡すと変えられます。

```csharp
[StructLayout(LayoutKind.Sequential)]
struct SET_SDR_WHITE { public HEADER h; public uint level; public byte finalValue; }

var req = new SET_SDR_WHITE
{
    h = Header<SET_SDR_WHITE>(-18, target.adapterId, target.id),
    level = level,       // 1000 = 80 nits
    finalValue = 1,
};
DisplayConfigSetDeviceInfo(ref req);
```

Windows の設定画面のスライダー 0〜100 は **80〜480 nits** に対応しているので、アプリの 0〜100% もそれに合わせました。

```csharp
double nits = 80 + 400 * percent / 100.0;
uint level = (uint)Math.Round(nits * 1000 / 80);
```

:::message alert
非公開の値なので、将来の Windows で動かなくなる可能性があります。アプリでは失敗したら通知を出し、「HDR のモニターも変える」をオフにすれば HDR のモニターには何もしないようにしています。また、**Windows の設定そのものを書き換える**ため、アプリを削除しても最後の値が残ることを README に明記しました。
:::

HDR のオン／オフはあとから切り替えられることもあるので、15 秒ごとの確認のついでに HDR がオンのモニターの一覧を調べ、変わっていたら明るさを設定し直しています。

## 「なめらかに変化」は smoothstep で

「22:00 に 30%（なめらか）」なら、直前の項目（たとえば 12:00 に 100%）からの間を補間します。最初は直線で補間していましたが、変わり始めと終わりがカクッとするので、**smoothstep** にしました。

```csharp
double f = elapsed / span;      // 0〜1
f = f * f * (3 - 2 * f);        // ゆっくり始まって、ゆっくり終わる
return prev.Brightness + (next.Brightness - prev.Brightness) * f;
```

ちょうど中間の時刻の値は直線と同じなので、「17 時には 65%」のような見当はそのままつきます。画面のグラフも同じ関数で描いているので、グラフと実際の動きがずれません。

![明るさの編集画面](images/edit.png)

## 2 台のモニターの明るさをそろえる補正

同じ 50% でも、液晶と有機 EL、メーカーの違いで見え方はかなり違います。そこで、スケジュールの値をモニターごとに変換してから設定するようにしました。

| 方式 | 変換 |
| --- | --- |
| 範囲で指定 | `min + (max - min) * p / 100` |
| ずらす | `p + shift` |
| 倍率 | `p * factor / 100` |

補正は**モニターを見分ける ID**ごとに保存します。DDC/CI 側は前述の `EnumDisplayDevices` のデバイスインターフェイス名、HDR 側は `DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorDevicePath` を使っていて、どちらも `\\?\DISPLAY#...#{...}` という同じ形式です。そのため、**HDR をオン／オフしても同じ補正が使われます**。

「範囲で指定」は数字だけだと使い方が伝わりにくかったので、ガイドを付けました。確認用の明るさを自動で 0% → 100% に切り替えながら、合わせる行を枠で示して、実際の画面を見比べてもらいます。

![モニターごとの補正のガイド](images/correction.png)

## おわりに

夜に画面がまぶしいと感じることがなくなり、目がかなり楽になりました。DDC/CI に対応したモニターと Windows があれば追加のソフトなしで動くので、よかったら使ってみてください。

不具合の報告や「こんな機能がほしい」という要望は、GitHub の Issues でお待ちしています。

このアプリが気に入っていただけたら、[OFUSE](https://ofuse.me/c0fdccc4) から開発を応援していただけるとうれしいです。
