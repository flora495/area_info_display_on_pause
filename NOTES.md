# Pause Location Display 調査メモ

逆コンパイル（ilspycmd, `JumpKing.exe` v実機インストール済みのもの）による調査の経緯と、実現可能性の結論をまとめたもの。

**実装状況**: 本実装はこの調査結果に基づいて完了し、ビルドも通っている（リポジトリルートの各`.cs`ファイル）。ただし実機（Jump King本体）での動作確認はまだ行っていない。下記「未検証事項」は実装後も引き続き未検証のまま。

## 【重要・厳守】実装時の制約（他modから引き継ぎ）

このmodもSteam Workshop上での公開を想定する。**Jump King本体のプログラムやファイル（`C:\Program Files (x86)\Steam\steamapps\common\Jump King\`配下など）は一切変更・上書き・削除してはならない。** 実装は必ず `area_info_display_on_pause/` フォルダの下だけで行うこと（ビルド参照のための読み取り専用パス指定を除く）。本体への変更が必要に見える場合は、Harmonyによる実行時パッチなど、本体ファイルを書き換えない手段で実現する（[keep_babe_skin](../keep_babe_skin/NOTES.md)・[confirm_count_control](../confirm_count_control/MenuFactoryPatches.cs)と同じ方針）。

## やりたいこと（仕様）

Pause中の画面に、以下を表示する。

1. 現在いるエリアの名前
2. そのエリアの「何枚目」にいるか

現在のスクリーンが「エリア定義の範囲内」か「次のエリアへ向かう途中のすき間」かを自動判定し、すき間では直前のエリア名を引き継いで枚数だけ伸び続ける表示にする（詳細な判定アルゴリズムは「確定した仕様」の節を参照）。

範囲内にいる時に総数（`m`）を表示するかどうかは、メインメニューから3パターンの中から選べる設定にする（「常に表示」「表示しない」「そのエリアを突破した後だけ表示」。詳細は「追加要望3」の節を参照）。さらに、エリアへの挑戦回数（`(k回目)`）を併せて表示する機能も用意し、これもON/OFFを切り替えられるようにする（詳細は「追加要望2」の節を参照）。

## 調査結果サマリ（結論: 実現可能。エリアの概念はゲーム本体の`location_settings.xml`機構がそのまま使える）

| 必要な情報 | ゲーム側にあるか | 取得方法 |
|---|---|---|
| 現在の画面（スクリーン）の絶対インデックス・合計スクリーン数 | **ある**（public API） | `JumpKing.Camera.CurrentScreen` / `CurrentScreenIndex1`、`JumpKing.LevelManager.TotalScreens` |
| 現在プレイ中のレベル（マップ）の識別・名前 | **ある**（public API） | `Game1.instance.contentManager.level`（`JumpKing.Workshop.Level : IUGC`）の`Name`/`ID`/`Root`。バニラ時は`null`になるため`MenuFactory.GetLevelTitle()`相当のフォールバックが必要 |
| 「エリア」という区切り・エリア名・エリアの境界（開始/終了スクリーン） | **無い** | ゲーム内データに一切存在しない（後述）。mod側で独自に用意する必要がある |
| Pause中のUIにテキストを追加する手段 | **ある**（Harmonyパッチ + public API） | `MenuFactory.CreateStatsDisplay`等をHarmonyでpostfixパッチし、`DisplayFrame.AddChild`で独自`TextInfo`を追加 |

技術的な障壁は無い。最大の論点は「エリア」の境界をどう定義・管理するかという**mod側の設計判断**であり、これは実装着手前にユーザーに確認が必要（後述の「要確認事項」）。

## 詳細調査

### 1. 現在のスクリーン位置・合計スクリーン数（public、パッチ不要）

`JumpKing.Camera`（`public static class`）:

```csharp
public static int CurrentScreen { get; }       // 0始まり。現在の絶対スクリーンインデックス
public static int CurrentScreenIndex1 { get; }  // 1始まり（CurrentScreen + 1）
```

`JumpKing.LevelManager`（`public class`）:

```csharp
public static int TotalScreens { get; }  // レベル全体の合計スクリーン数
```

`TotalScreens`は`LevelManager.LoadScreens()`内で`LevelTexture.GetTotalScreensPotential()`（レベルテクスチャの縦サイズから算出）によって決まり、画面ロード時に確定する。バニラ・カスタムレベル問わず同じ仕組み。

これらは完全に`public`なstaticプロパティであり、**Harmonyパッチなしで直接参照できる**（読み取りのみなので副作用の心配も無い）。

### 2. レベル（マップ）の識別

`JumpKing.JKMemory.JKContentManager`（厳密な名前空間は要再確認だが`JKContentManager`クラス）に:

```csharp
public string root = "Content";          // カスタムレベル再生中はそのレベルのフォルダ、通常時は"Content"
public JumpKing.Workshop.Level level;    // カスタムレベル再生中のみ非null。バニラ本編プレイ中はnull
```

`JumpKing.Workshop.Level : IUGC`は以下を持つ（`IUGC`基底で定義）:

```csharp
public string Name { get; }    // Steam Workshopのアイテムタイトル
public ulong ID { get; }       // Steam Published File ID（安定した一意キーとして使える）
public string Root { get; }    // ディスク上のフォルダパス
```

バニラ本編（`level == null`）の場合の表示名取得は、既存コード`JumpKing.PauseMenu.MenuFactory.GetLevelTitle()`が参考になる:

```csharp
public string GetLevelTitle()
{
    if (Game1.instance.contentManager.level != null)
        return Game1.instance.contentManager.level.Name;
    if (EventFlagsSave.ContainsFlag(StoryEventFlags.StartedGhost))
        return language.GAMETITLESCREEN_GHOST_OF_THE_BABE;
    if (EventFlagsSave.ContainsFlag(StoryEventFlags.StartedNBP))
        return language.GAMETITLESCREEN_NEW_BABE_PLUS;
    return language.GAMETITLESCREEN_NEW_GAME;
}
```

→ 「現在プレイ中のレベル」を一意に識別するキーとしては、カスタムレベルなら`level.ID`（無ければ`level.Root`にフォールバック）、バニラ本編なら上記3分岐（NewGame / Ghost of the Babe / New Babe Plus）を使う想定。

### 3. 「エリア」はゲーム側に存在しない

`JumpKing.exe`全体（36000行超の逆コンパイル結果）を"area"/"zone"/"region"等で検索したが、**該当する概念は一切存在しなかった**。スクリーン単位の管理（`LevelScreen`配列、絶対インデックス）はあるが、それを「エリア」という単位でグルーピングする仕組みはゲーム本体・カスタムレベルのデータ形式（`level_settings.xml`等）のどちらにも存在しない。

つまり「フォレスト」「塔」のようなエリア名は、コミュニティが便宜上呼んでいる名称であり、ゲームから取得することはできない。**このmod自身が「スクリーンインデックスの範囲 → エリア名」の対応表を独自に持つ必要がある。**

これが今回の調査で最も重要な発見であり、後述の「要確認事項」に直結する。

### 4. Pause中UIへの描画追加方法

`JumpKing.PauseMenu.PauseManager`（`internal`）が`MenuFactory`（`internal`）の`CreateStatsDisplay(GuiFormat)`・`CreatePauseInfo()`を呼んでPause画面のUIを構築している。

`CreateStatsDisplay`の実装（抜粋、`public`メソッドだがクラス自体は`internal`）:

```csharp
public DisplayFrame CreateStatsDisplay(GuiFormat p_format)
{
    DisplayFrame displayFrame = new DisplayFrame(p_format, BTresult.Running);
    if (LevelDebugState.instance == null)
        displayFrame.AddChild(new TextInfo(GetLevelTitle(), Color.Gray));
    displayFrame.AddChild(new SessionInfo(StatType.Current));
    displayFrame.AddChild(new TimeInfo(StatType.Current));
    displayFrame.AddChild(new JumpInfo(StatType.Current));
    displayFrame.AddChild(new FallInfo(StatType.Current));
    displayFrame.Initialize();
    m_drawables.Add(displayFrame);
    return displayFrame;
}
```

レベル名（タイトル）の直下に統計情報（セッション数・時間・ジャンプ数・落下数）を並べている箇所であり、「エリア名 n/m」もここに1行追加するのが最も自然。

**注入方法**: `MenuFactory`は`internal class`なので、`confirm_count_control/MenuFactoryPatches.cs`と同じ手法（`AccessTools.TypeByName("JumpKing.PauseMenu.MenuFactory")` → `AccessTools.Method(...)`）でHarmonyのpostfixを当てる。`CreateStatsDisplay`の戻り値`DisplayFrame`は`public`型なので、postfix側で`__result`を直接操作できる。

**注意点（要実装時に踏まえる）**: `DisplayFrame.AddChild<T>`はpublicだが、`CreateStatsDisplay`内で**`displayFrame.Initialize()`が呼ばれた後に`return`される**。`Initialize()`は内部の`m_menu_items`配列・描画範囲(`m_bounds`)をその時点の子要素数で確定する処理なので、postfixで`__result.AddChild(...)`した**後に`__result.Initialize()`をもう一度呼び直す**必要がある（呼び直さないと追加した行が描画されない、または枠のサイズ計算がずれる）。`Initialize()`は副作用の無い再計算のみなので再呼び出しは安全（`keep_babe_skin`の`KingSprites`コンストラクタJIT問題のような複雑な落とし穴は無いはずだが、実機未検証）。

### 5. 表示テキストを毎フレーム更新する方法

`JumpKing.PauseMenu.BT.Stats.IStatInfo`（`SessionInfo`/`TimeInfo`/`JumpInfo`/`FallInfo`の基底、`public abstract class IStatInfo : TextInfo`）が使っている既存パターンがそのまま流用できる:

```csharp
public abstract class IStatInfo : TextInfo
{
    public override void Draw(int x, int y, bool selected)
    {
        base.Text = GetLabel();   // 描画直前に毎回テキストを再計算
        base.Draw(x, y, selected);
    }
    protected abstract string GetLabel();
}
```

→ 同様に`TextInfo`を継承した独自クラス（例: `AreaPageInfo`）を作り、`Draw`/`GetSize`の中で`Camera.CurrentScreen`から現在のエリア・枚数を再計算してテキストを更新する。Pause中は実質スクリーンが変わらないはずだが、この方式なら万一変化しても自動的に追従し、かつ既存コードと一貫したスタイルになる。

## 仕様として固めたいデータモデル（提案）

レベルごとに「エリア定義」のリストを持つ。1エリアは以下を持つ想定:

```
- name: エリア名（表示用文字列）
- start_screen: そのエリアの開始スクリーンインデックス（絶対値）
- end_screen: そのエリアの終了スクリーンインデックス（絶対値・省略可）
```

- `end_screen`が**設定されている**場合 → パターンA（`n/m`）。`m = end_screen - start_screen + 1`、`n = 現在のCurrentScreen - start_screen + 1`
- `end_screen`が**省略されている**場合 → パターンB（`n`のみ）。`n = 現在のCurrentScreen - start_screen + 1`
- 現在のスクリーンがどのエリア定義にも当たらない場合 → 表示しない（or "Unknown"等のフォールバック、要確認）

レベル識別キー（上記「2.」参照）ごとにこのエリア定義リストを切り替える。バニラ本編とカスタムレベルで別々の定義セットを持つ。

## 訂正: 「エリア」は実はゲーム側に既存の仕組みがあった（`LocationSettings`）

上記「3. 『エリア』はゲーム側に存在しない」は、`Camera`/`LevelManager`などコア部分だけを"area"等のキーワードで検索した際の結論であり、**不正確だった**。ユーザーからの指摘で`Content/gui/location_settings.xml`の実在を確認し、これを起点に調査し直した結果、ゲーム本体に最初から「画面の絶対インデックス範囲 → エリア名」を表すデータ形式と、それを使ったランタイムの仕組み（パース中の地名表示ポップアップ機能）が存在することが分かった。

### `LocationSettings`／`Location`の実体

`JumpKing.MiscSystems.LocationText`名前空間（逆コンパイル結果より）:

```csharp
public struct Location
{
    public int start;   // 開始スクリーン（1始まりの絶対インデックス、Camera.CurrentScreenIndex1と同じ基準）
    public int end;     // 終了スクリーン（int。Nullableではない）
    public int unlock;  // このエリアを「新規発見」として通知するスクリーン番号
    public string name; // 表示名。バニラは言語リソースキー（例: "LOCATION_REDCROWN_WOODS"）、カスタムレベルは生の文字列がそのまま入っていることが多い
}

public struct LocationSettings
{
    public Location[] locations;
}

internal class LocationTextManager : Entity
{
    private static LocationSettings _settings;
    public static LocationSettings SETTINGS { get { return _settings; } }  // publicなstaticプロパティ
    public static void SetSettingsData(LocationSettings p_settings) { _settings = p_settings; }
}
```

ロードは`JKContentManager.MiscSettings.Load`が毎レベルロード時に必ず呼ぶ:

```csharp
public class MiscSettings
{
    public void Load(ContentManager p_loader)
    {
        string text = "gui/location_settings";
        LocationTextManager.SetSettingsData(
            XmlSerializerHelper.Deserialize<LocationSettings>(Game1.instance.contentManager.root + "/" + text + ".xml"));
    }
}
```

`root`はバニラ本編プレイ時`"Content"`、カスタムレベルプレイ時はそのレベルのインストール済みフォルダの絶対パスになる（`keep_babe_skin`調査時の知見と同じ）。つまり**この読み込み処理自体がバニラ・カスタムレベルの両方に自動的に対応しており、mod側で「どのレベルかを判別してデータを切り替える」処理を自前で書く必要が無い**。`LocationTextManager.SETTINGS.locations`を読むだけで、その時プレイ中のレベルに対応するエリア一覧が常に得られる。

「現在のエリア」の判定方法も既存コード（`LocationComp.GetCurrentLocations()`）にそのまま書かれている:

```csharp
int currentScreenIndex = Camera.CurrentScreenIndex1;  // 1始まり
foreach (var item in m_settings.locations)
{
    if (currentScreenIndex >= item.start && currentScreenIndex <= item.end) { /* マッチ */ }
}
```

表示名の解決も既存コードに実装例がある:

```csharp
string text = language.ResourceManager.GetString(location_name);
// 言語リソースに無ければ生の文字列をそのまま使う（カスタムレベルはこちらに該当することが多い）
string displayName = text ?? location_name;
```

### 実機データでの検証結果

- `Content/gui/location_settings.xml`（バニラ本編）: 26エリア定義済み、全エントリに`start`/`end`/`unlock`/`name`が揃っている。
- インストール済みカスタムレベル（`...\steamapps\workshop\content\1061090\<id>\`）を**全件**調査（`level.xnb`を持つ＝実際にプレイ可能なレベル54件中）:
  - **53/54件**が`gui/location_settings.xml`を同梱していた（残り1件は古いレベルらしく`gui`フォルダ自体は存在するが空、おそらくこの仕組みが追加される前に作られたもの）
  - 同梱されている53件の`gui/location_settings.xml`に含まれる`<Location>`タグの総数（450件）と`<end>`タグの総数（450件）を全ファイルで突き合わせたところ**完全に一致**。つまり**サンプル内では`end`が省略された`<Location>`エントリは1件も存在しなかった**
  - `<locations>`が空（0件）のファイルも1件確認した（レベルエディタが生成するデフォルトのテンプレートと思われる）
  - `name`の値はバニラ風の言語キーではなく、`"Icy Caverns"`や`"Awesome first area"`のような生の表示用文字列がそのまま入っているケースが大半だった（前述の`language.ResourceManager.GetString`によるフォールバックがまさにこのケースに対応する設計だと裏付けられた）

### 重要な含意: ネイティブデータだけでは「総数不明（パターンB）」が原理上ほぼ発生しない

`Location.end`は非Nullableの`int`であり、実例調査でも省略されたケースが見つからなかった。つまり、ある画面が`Location`にマッチする場合、**そのエリアの総枚数（`end - start + 1`）は常に確定できる**ことになり、ユーザーが希望した「パターンB（現在の枚数のみ、エリアの総数は不明）」が、このネイティブデータだけからは原理上ほとんど発生しない。

パターンBが現実に発生しうるとすれば:

1. **`<Location>`に`<end>`タグが無い、または`end`が`start`未満などの異常値**（XMLSerializerはint型に対応する要素が無ければ`0`のまま残すため、`end`要素を省略したXMLを誰かが書けば発生する。今回のサンプルには実例なし＝レアケース）。
2. **現在のスクリーンがどの`Location`の`[start, end]`にも入らない「すき間」にいる場合**（バニラの実データにも`THE_TOWER`(end=43)と`BRIGHTCROWN_WOODS`(start=47)の間の44-46番スクリーンのように、定義上の「すき間」が複数存在する）。ただしこのケースは別の質問で「何も表示しない（行自体を出さない）」と既に決定済みであり、「パターンB」とは別の扱いになっている。

## 確定した仕様（ユーザー回答済み）

### パターンBの最終定義

「未定義区間では直前に確定していたエリア名を出し続ける」方式に決定（前述の「要確認事項1」の(b)案）。これにより「画面自体を非表示にする」という当初の決定は撤回し、以下のロジックに統一する。

判定アルゴリズム:

1. `locations = LocationTextManager.SETTINGS.locations`を`start`昇順でソートする
2. `idx = Camera.CurrentScreenIndex1`（1始まり）を取得する
3. `start <= idx <= end`を満たす`Location`があれば、それが**現在のエリア**＝**パターンA**。
   - `表示名 = language.ResourceManager.GetString(name) ?? name`
   - `現在の枚数 = idx - start + 1`
   - `総枚数 = end - start + 1`
   - 表示: `表示名 n/総枚数`
4. 満たす`Location`が無ければ、`start <= idx`を満たす（＝`idx`より手前から始まっている）`Location`のうち`start`が最大のものを探す。見つかれば**パターンB**。
   - `表示名`は同上
   - `現在の枚数 = idx - start + 1`（`end`を超えて伸び続ける値になる）
   - 表示: `表示名 n`（総枚数は表示しない）
5. （3）も（4）も無い場合（＝`idx`が最初の`Location.start`より手前。実例上ほぼ起こらないはずだが理論上は有り得る）→ 表示する行自体を出さない

### データ取得方法

- 独自のエリア設定ファイルは不要。バニラは`Content/gui/location_settings.xml`、カスタムレベルはそのレベル自身の`gui/location_settings.xml`を、ゲーム本体と同じ`LocationTextManager.SETTINGS`（`internal`クラスの`public static`プロパティ。Harmony/`AccessTools`のリフレクションで読む）からそのまま取得する
- 対応レベルは「バニラ＋特定のカスタムレベル」。上記の仕組みによって実質「`gui/location_settings.xml`を同梱している全レベル」に自動対応できる
- `gui/location_settings.xml`が存在しない・空のカスタムレベル（実機調査で1件確認済み）では、`locations`が空配列になり上記アルゴリズムの（5）に該当 → 行自体を表示しない（追加確認不要、この挙動で問題ない）

### UI・トグル

- メインメニューにON/OFFトグルを追加する（既存2 modと統一）
- 表示位置は`MenuFactory.CreateStatsDisplay`内、レベル名の直下（既存の統計情報と並ぶ場所）を第一候補とする。色・フォントは既存の`Color.Gray`系に合わせる想定（細部は実装時に調整）

## 追加要望1: 表示位置を「Objective」欄（画面下部）に変更

`MenuFactory.CreatePauseInfo()`が、ユーザーの言う「Objective: get to the babe at the top!」を表示している箇所だと特定した:

```csharp
public DisplayFrame CreatePauseInfo()
{
    GuiFormat gUI_FORMAT = GUI_FORMAT;
    gUI_FORMAT.all_margin /= 2;
    gUI_FORMAT.all_padding /= 2;
    gUI_FORMAT.all_padding++;
    gUI_FORMAT.element_margin = 0;
    gUI_FORMAT.anchor = new Vector2(0.5f, 1f);   // ← 画面下部中央アンカー
    DisplayFrame displayFrame = new DisplayFrame(gUI_FORMAT, BTresult.Running);
    displayFrame.AddChild(new TextInfo(language.MENUFACTORY_OBJECTIVE, Color.White));
    displayFrame.Initialize();
    m_drawables.Add(displayFrame);
    return displayFrame;
}
```

`anchor = (0.5f, 1f)`（水平中央・垂直下端）という指定が「画面下部」という見た目と一致しており、`CreateStatsDisplay`（画面端に縦に並ぶ統計情報欄）とは別の箇所であることを確認した。**表示場所をここに変更する場合、パッチ対象を`CreateStatsDisplay`ではなく`CreatePauseInfo`に変更する**（注入方法・`Initialize()`再呼び出しの必要性などの技術的な注意点は前述のものと同様）。

`language.MENUFACTORY_OBJECTIVE`の実際の文字列値は`LanguageJK.dll`内の埋め込みリソース（.resx相当）にあり、今回はilspycmdの標準逆コンパイルでは文字列の中身までは確認できなかったが、`CreatePauseInfo`の構造（アンカー位置・常時1行表示）から見て、ユーザーが指す「Objective」表示と同一箇所であることはほぼ間違いない。

## 追加要望2: エリアへの「挑戦回数」表示

### 要件の整理

- そのエリアに最初に入ったとき: 1
- そのエリアより下に落ちた後、再びそのエリアに入ったとき: +1
- 画面（スクリーン）の数字の並びは、必ずしもプレイ順（攻略順）と一致しない（後述で実例を確認）

### 訂正: 「配列順＝実際のプレイ順」という結論は撤回する

当初、バニラの`Content/gui/location_settings.xml`で`<Location>`タグの並び順が数値（`start`）の昇順になっていない箇所（`THE_TOWER`(40-43)の次に`PHILOSOPHERS_FOREST`(157-160)、その次に`BOG`(102-108)…と続く、「owl」コメント以降の箇所）を見つけ、かつゲーム自身の「エリア新規発見」ポップアップ機能（`LocationComp.CheckIfNewScreen`）がこの配列順を進行度（`best_location`という配列インデックス）として使っていることから、「配列順＝実際のプレイ順」と結論付けた。**この結論はユーザーからの指摘により撤回する。**

問題点: `CheckIfNewScreen`は配列順を使って`best_location`を進めているが、これは「配列順が正しければ正常に動く」という話であって、「配列順が必ず正しい（＝実際のプレイ順と一致する）」ことの証明にはならない。この機能はポップアップ表示タイミングがズレるだけの軽微な見た目の機能であり、順序を間違えてもゲームがクラッシュしたり進行不能になったりしないため、レベル制作者が誤った順序のまま気づかずに公開していても誰も気づかない可能性が十分にある。要するに「ゲームが配列順に依存している」ことは事実だが、「だから配列順は信頼できる」という推論は誤り（依存していることと、その前提が常に正しく満たされていることは別の話）。

### 確実に言えること（エンジンの物理的な仕様として）

`JumpKing.Camera`の実装（`UpdateCameraWithVelocity`内の`int num = -(int)Math.Floor(p_center.Y / 360f)`）により、**ワープが一切絡まない限り、スクリーン番号の増減は「登る/落ちる」という物理的な上下動と完全に一致する**。これは制作者の意図とは無関係な、エンジンの計算上の事実である。つまり、ある区間が「ワープで繋がれていない、地続きの一本道」であれば、その区間内では数値の`start`順＝実際のプレイ順であることが保証される。

### ワープ（`TeleportLink`）の実体を確認した

数値順と実際のプレイ順がズレる可能性がある唯一の原因は、レベルテクスチャに埋め込まれた**ワープ**である。逆コンパイルで実体を確認した:

- `LevelManager.LoadBlocksInterval`が各画面の60x45ブロックを走査し、色が`(R=行き先スクリーン番号(1始まり), G=0, B=255)`のブロックを見つけると、そのブロックのX座標（30ブロック未満か以上か＝画面の左半分か右半分か）に応じて`teleport[0]`（左用）または`teleport[1]`（右用）に`TeleportLink(R値)`を設定する
- 実際にワープが発生するのは`HandlePlayerTeleportBehaviour.ExecuteBehaviour`で、プレイヤーが画面の**左右の端**（`X < 0`または`X > 480`）まで歩いて出た時。つまりこれは「画面の左右端を出ると別の画面にワープする」横方向の仕組みであり、踏むと発動するワープパッドのようなものではない
- ワープ先は`TeleportLink`が保持する画面番号（任意の番号を取れる。隣接画面である必要はない）

つまり、**ある区間の数値順とプレイ順がズレているかどうかは、その区間の画面（特に左右端）にこのワープ用の特殊ピクセルが埋め込まれているかどうかを実際にテクスチャデータを読んで確認しないと分からない**。バニラの「owl」区間（157→102のジャンプ）がこのワープで実現されているのか、それとも単なる配列の記述順の問題（実際のプレイ順とは無関係）なのかは、今回の調査では確認できていない（レベルテクスチャのピクセルデータを読むには実機でのデバッグ、もしくはテクスチャファイルを画像として開いて該当ブロックの色を直接確認する必要があり、今回はそこまで到達していない）。

### 設計への影響

「配列順をそのまま進行順として使えば追加データ不要」という当初の結論は使えない。挑戦回数機能の「進行順」をどう決めるかについて、改めてユーザーに確認する必要がある（後述の要確認事項）。

### 挑戦回数カウントの判定アルゴリズム（提案・「進行順ランク」の決め方は別途確認が必要）

判定ロジック自体は「進行順ランク」という抽象値さえ各エリアに割り振れれば、その出典が何であっても同じロジックで動く。`order(area)`を「そのエリアの進行順ランク（値が大きいほど後で訪れる想定）」とする（具体的に何を使うかは後述の要確認事項）。

毎フレーム（`LevelManager.Update`内、後述）以下を行う:

1. `newArea = ResolveCurrentLocation()`（マッチしたLocation、Pattern Bのすき間では直前にマッチしていたLocationを使う。マッチが無ければ何もしない）
2. `lastArea`（前回フレームの結果。レベルロード時は`null`）と比較
3. `lastArea == null`、または`order(lastArea) < order(newArea)`（より進行順が手前のエリアから、より先のエリアに入った＝「下から登ってきた」）の場合:
   - そのエリアの挑戦回数が未記録なら`1`を設定（初回到達）
   - 既に記録があれば`+1`（一度退いてから登り直してきた）
4. `order(lastArea) > order(newArea)`（進行順がより手前のエリアに移った＝「下に落ちた」）の場合: 回数は変更しない（このタイミングでは増やさない。登り直して再進入した時に(3)で増える）
5. `lastArea == newArea`: 何もしない
6. `lastArea = newArea`に更新

この1本のルールだけで「初回入場時は1」「下に落ちてから再進入したら+1」「単に手前のエリアへ戻っただけ（まだ本格的に下に落ちていない）の場合はそのエリア自身の回数は増えない」を自然に表現できる。**ただし`order(area)`の出典をどう決めるかは未確定**（後述の要確認事項を参照）。

### 継続的なフレーム更新フックの確保

挑戦回数の判定はポーズ画面を開いていない通常プレイ中も継続して動く必要がある。`JumpKing.LevelManager.Update(float p_delta)`（`public static`）が最適なフック先だと確認した:

```csharp
public void Update(GameTime gameTime)
{
    ...
    if (PauseManager.instance == null || !PauseManager.instance.IsPaused)
    {
        LevelManager.Update(p_delta);   // ← ポーズ中は呼ばれない、それ以外は毎フレーム呼ばれる
        ...
    }
}
```

`LevelManager`は`public class`であり、`Update`も`public static`なので、属性ベースの通常のHarmonyパッチ（`[HarmonyPatch(typeof(LevelManager), nameof(LevelManager.Update))]`、`AccessTools`によるリフレクションは不要）でpostfixを当てるだけでよい。ポーズ中は自動的に呼ばれなくなる点も、画面が止まっている間は判定が進まないという直感的な挙動と一致しており都合が良い。

### 挑戦回数データの永続化（セーブ/ロード）

ゲーム自身の`SaveLube`（`internal static class`）が、似た性質を持つ「現在の進行度」（`LocationState.best_location`等）を`Content/Saves/combined.sav`に保存している。`SaveLube`自体は`internal`だが、保存・削除のトリガーとなる以下のメソッドは`public static`であり、`MenuFactory`と同じ`AccessTools.TypeByName`方式でHarmonyパッチできる:

```csharp
public static void SaveCombinedSaveFile() // 1秒おきの自動保存時、およびゲーム終了時に呼ばれる
public static void DeleteSaves()          // 「Give Up」やニューゲーム開始等で呼ばれる（セーブの全消去）
public static void ProgramStartInitialize() // ゲーム起動時に一度だけ呼ばれ、combined.savをロードする
```

この3つにそれぞれHarmonyのpostfixを当てることで、**ゲーム本体のセーブ・ロード・削除のタイミングと完全に同期した形で、mod独自の挑戦回数データを保存・復元・リセットできる**:

- `ProgramStartInitialize`postfix → mod独自のXMLファイル（例: `F.AreaInfoDisplayOnPause.AreaAttempts.xml`、他mod同様mod DLLと同じフォルダに保存）を読み込み、メモリ上の辞書を復元
- `SaveCombinedSaveFile`postfix → メモリ上の辞書を同じファイルに書き出し（ゲーム自身のオートセーブと同じ頻度で保存される）
- `DeleteSaves`postfix → メモリ上の辞書をクリアし、ファイルも削除（ニューゲーム・Give Up時にゲーム本体の進行度と一緒にリセットされる）

これにより、mod独自のセーブファイルを持ちながらも、**ゲーム本体の「現在のプレイ進行」のライフサイクルと完全に同期する**（新しいセーブを始めれば0から、続きから始めれば前回の回数を引き継ぐ）。

データ構造は「キー（レベル識別子 + エリアの識別子(`start`値など、Locationを一意に表せるもの)） → (初回訪問順`order`, 挑戦回数)」の辞書で十分。レベル識別子は前述の「2. レベル（マップ）の識別」の節で調査済みの方法（`level.ID`/`level.Root`、バニラは3分岐）を流用する。`order`の決め方は後述の通り、事前データではなく実プレイ中の観測値を使う。

### この機能に関する確定した仕様（ユーザー回答済み）

- **表示フォーマット**: `エリア名 n/m (k回目)`。すき間にいる場合は`エリア名 n (k回目)`。（※「追加要望3」で導入した総数表示3パターン設定により、範囲内にいる場合でも設定によっては`n/m`ではなく`n`になる。`n/m`になるのは「常に表示」、または「突破後に表示」かつ突破済みの場合のみ）
- **すき間中の挑戦回数**: 直前にマッチしていたエリアの回数をそのまま表示し続ける（エリア名・枚数と同じ「直前のエリアを引き継ぐ」ルールに統一）。

### 確定: `order(area)`は「このプレイ中で最初に訪れた順番」を実測して使う（追加データ不要）

ユーザー案: 事前にプレイ順を調べて静的データとして用意するのではなく、**mod自身が実際のプレイ中に「各エリアに初めて入った順番」を観測し、その連番をそのまま`order(area)`として使う**方式に決定した。

```
order(area) = そのプレイ（セーブ）中で、そのエリアに初めて入った時に振られた連番（0, 1, 2, ...）
```

挑戦回数のカウントにはそもそも「各エリアを訪れた履歴」の記録が必要なので、**そのために使うデータをそのまま`order(area)`として転用できる**。レベルごとの事前準備（数値順の決定・手動オーバーライド・テクスチャ解析）は一切不要になる。

#### この方式が成立する条件・限界

- 前提: 「初回の探索」が実際の物理的な上下動の順序と一致していること。つまり、**初めて訪れる時に、ワープでまだ訪れていない先のエリアへ飛ばされない**限り、観測した訪問順は常に正しい進行順になる
- 限界: 初回探索時にワープでより先のエリアへ飛ばされるような特殊な構成のレベルでは、その時だけ`order`が実際の空間的な並びと矛盾し、以後の判定がズレる可能性がある。これは前述のいずれの代替案（数値順固定・手動オーバーライド・テクスチャ解析）でも完全には避けられない種類の難しさであり、観測ベースの方式が特に不利になるわけではない。発生頻度も低いと考えられるため、現時点では許容する

#### 実装上の補正: セーブからの再開時に誤って「初回侵入」と判定しないようにする

判定アルゴリズムの(3)は「`lastArea == null`の場合は初回扱い」としているが、これは**レベルロード直後・セーブからの再開直後にも`lastArea`が`null`になる**ことを意味する。何の補正もしないと、エリアの途中でセーブ＆再開しただけで、そのエリアの挑戦回数が誤ってカウントアップされてしまう。

これを避けるため、レベルロード時（`[OnLevelStart]`等）に一度だけ、**現在地を「静かに」（カウントを変えずに）`lastArea`へ設定する処理**を行う:

1. レベルロード直後、`ResolveCurrentLocation()`で現在地を取得
2. その場所がまだ`order`未登録（＝このセーブで一度も訪れていない＝本当に真新しいセーブの最初の地点）なら、`order`を新規登録し、挑戦回数を`1`に設定
3. すでに`order`が登録済み（＝続きのセーブで、前回終了時にいた場所に戻ってきた）なら、回数は変更せず`lastArea`だけ設定する

この一手間を入れることで、「セーブ＆再開」と「実際にそのエリアへ再進入した」を正しく区別できる。

### 負荷についての検討

`LevelManager.Update`へのpostfixは60fps想定で毎フレーム呼ばれるため、ここで行う処理の重さを検討した。

- 毎フレームの処理: `Camera.CurrentScreenIndex1`の取得（O(1)）→ `LocationTextManager.SETTINGS.locations`（バニラ26件、カスタムレベルでも数件〜20件程度）を線形走査して現在地を判定（O(n)、nは数十程度）→ 直前のエリアとの比較（O(1)）→ エリアが変わった時だけ辞書の読み書き（O(1)）。1秒あたり数千回程度の単純な整数比較に過ぎず、ゲーム本体が同じ`LevelManager.Update`内で毎フレーム行っている当たり判定（1画面60×45マスのブロック配列との衝突チェック）と比べて桁違いに軽い
- 永続化（ファイルI/O）: ゲーム自身の`SaveLube.SaveCombinedSaveFile()`（1秒に1回しか呼ばれない）に便乗させる設計のため、保存頻度は毎フレームではなく1Hz。保存内容も数十エントリ程度の小さな辞書で、ゲーム本体の`combined.sav`書き込みと同程度の負荷
- 実装上の注意点（負荷というより品質面）: 毎フレーム呼ばれるホットパスではLINQ（`.FirstOrDefault`等）や文字列結合のような暗黙のメモリ割り当てを避け、単純な`for`ループで書く。これを徹底すればGC負荷の増加も避けられる
- 表示用テキストの再計算（`TextInfo.Draw`オーバーライド）はポーズ中のみ呼ばれるため、そもそも毎フレーム駆動ではない

結論: 想定している処理内容（小さな配列の線形走査・辞書操作・低頻度のファイルI/O）の負荷は、ゲーム本体が同じ箇所で既に行っている処理と比べて無視できるレベルであり、フレームレートへの影響は実質無いと考えられる。

### 他mod「More Saves」（Workshop ID 3239040787, Zebra.MoreSaves）との互換性について

`MoreSaves.dll`を逆コンパイルして実装を確認した。

- レベルごとに「Auto」セーブ（1つ、レベル開始時に自動保存）と「Manual」セーブ（同じレベルでも好きなタイミングで何個でも作成可能）を持つ仕組みで、`JumpKing.SaveLube`の`SaveCombinedSaveFile`/`DeleteSaves`にHarmonyパッチを当てて連動している
- 保存・復元の対象は**6つの既知ファイルを名前で個別指定**（`combined.sav`, `attempt_stats.stat`, `event_flags.set`, `general_settings.set`, `inventory.inv`, `perma_player_stats.stat`）。フォルダ単位の汎用コピーではないため、**this modが独自に持つファイルは、どこに置いてもMoreSavesの保存/復元対象には含まれない**
- これらの既存ファイル（`CombinedSaveFile`/`PlayerStats`/`GeneralSettings`等）の構造体を確認したが、mod用に汎用的に使える空きフィールドは存在しない（`old_men`・`location_state`・`screen_events`の登録済みスクリーンリストなど、いずれも特定用途専用）。既存フィールドへの相乗りは、本来の機能を壊すリスクがあり、かつこれらのファイルは毎回全体が上書き保存されるため非推奨と判断した

**決定（ユーザー回答済み）**: 現在の設計（レベル識別子をキーに含めた1つの独自ファイル）のまま、MoreSaves向けの追加対応は行わない。

- レベルを切り替える使い方には正しく対応できる（データがレベル識別子で区切られているため）
- 同じレベルで複数のManualセーブを作って使い分ける使い方には対応しない（どのManualセーブを読み込んでも、this modの記録は「最後に書き込まれた値」を共有する形になる）。MoreSavesと連携してこの取りこぼしを直接解消する実装（`MoreSaves.Saves.SaveManager`へのHarmonyパッチ追加）は今回は見送る

## 追加要望3: メインメニューからの設定項目（3つ）

ユーザーから、メインメニューのmod設定一覧（既存2 modの「Enabled」トグルが並んでいる箇所）から、以下3つを選択できるようにしたいという要望があった。

1. このmod自体を有効にするかどうか（ON/OFF）
2. 枚数の総数表示パターンを3種類用意し、メニューから選択できるようにする
3. 挑戦回数カウンターを有効にするかどうか（ON/OFF、上記1とは別に独立して切り替え可能）

### UI実装手段（既存modの流用で実現可能）

`confirm_count_control`が使っている2つの基底クラス（`JumpKing.PauseMenu.BT.Actions`名前空間）がそのまま使える。

- **`ITextToggle`**: ON/OFFの2値トグル。`EnabledToggle`（modの有効/無効）がこの実例。`GetName()`でラベル文字列、`OnToggle()`で切り替え時の処理を実装するだけでよい
- **`IOptions`**: 複数の選択肢をクリックで順送りに切り替える汎用オプション。`ConfirmCountOption`がこの実例（コンストラクタで`(選択肢の数, 現在値, EdgeMode.Clamp)`を渡し、`CurrentOptionName()`で現在の選択肢の表示文字列、`OnOptionChange(int option)`で選択肢が変わった時の処理を実装する）。選択肢数を2ではなく3にすれば、そのままこの要望の(2)に使える

→ (1)と(3)は`ITextToggle`を2つ用意するだけ、(2)は`IOptions`を1つ（選択肢3つ）用意するだけで実現できる。技術的な障壁は無い。

### (2) 総数表示3パターンの仕様

これまで「パターンA（エリア内・総数を表示）/パターンB（すき間・総数不明)」と呼んでいたのは、**現在のスクリーンがエリア定義の範囲内か、すき間かを自動判定する**ロジックだった。これはそのまま残る。

今回の要望は、**範囲内（パターンA相当）にいる時に、総数を表示するかどうかをユーザーが選べるようにする**という、別軸の設定。3つの選択肢:

| # | 名称（仮） | 範囲内にいる時の表示 | すき間にいる時の表示 |
|---|---|---|---|
| 1 | 常に総数を表示 | `n/m` | `n`（総数表示は元々できないため変化なし） |
| 2 | 総数を表示しない | `n` | `n` |
| 3 | 突破後に総数を表示 | そのエリアを**未突破**なら`n`、**突破済み**なら`n/m` | `n` |

「突破」の定義: そのプレイ（セーブ）中に、そのエリアの`end`スクリーンを一度でも超えたことがある（＝そのエリアの先、次のエリアやすき間に到達したことがある）。一度突破していれば、その後何度そのエリアに戻ってきても、以降は`n/m`表示になる（突破前の状態に戻ることはない）。

#### 実装方法

挑戦回数機能で「エリアの初回訪問順を観測して記録する」ために用意する、すでにエリア単位で永続化する仕組み（`order(area)`・挑戦回数を保存しているもの）に、もう1つのフラグ`hasFullyCleared(area): bool`を追加するだけで実現できる。判定・更新のタイミングは挑戦回数のロジックと全く同じ箇所（`LevelManager.Update`へのpostfixで毎フレーム行っている「現在地の解決」処理）に乗せられる:

- 現在地がエリアXの範囲内から、Xの`end`を超えた（次のエリアやすき間に移った）瞬間に、`hasFullyCleared[X] = true`にする
- セーブ再開時の補正（挑戦回数のために用意する「ロード直後は静かに現在地を記録するだけ」の処理）の中で、現在のスクリーンが`end`を超えているエリアがあれば、それらも`hasFullyCleared = true`として復元する（このmodを導入する前から先に進んでいた場合の取り残し防止）

追加の負荷は無視できるレベル（既存のフレーム毎の処理に、bool1個の比較・代入が増えるだけ）。

### 設定3つの相互関係

- (1)のmod全体トグルがOFFの場合、(2)(3)の設定値に関わらず何も表示しない（既存2 modと同じ「先頭でIsEnabledを見て即returnする」方式）
- (3)の挑戦回数カウンターがOFFの場合、表示文字列から`(k回目)`の部分を省く（エリア名と枚数のみ表示）。カウント自体の記録（`LevelManager.Update`内の処理）も止めてよい（OFF中は数えない。OFF→ON時に「これまでの分」が抜けるが、これは仕様として許容する想定）

### 確定したデフォルト値（ユーザー回答済み）

- (1)mod有効 = ON
- (2)表示パターン = 「突破後に総数を表示」
- (3)挑戦回数カウンター = ON

## 未検証事項（実機での確認が必要）

- `MenuFactory.CreateStatsDisplay`（または`CreatePauseInfo`）へのHarmony postfixパッチが実際に効くか（`MenuFactory`内部の他メソッドとの相互作用、JITインライン化等の問題が`keep_babe_skin`のケースのように発生しないか）
- `DisplayFrame.AddChild`後の`Initialize()`再呼び出しで実際に正しく描画・レイアウトされるか
- `LocationTextManager.SETTINGS`をリフレクション（`AccessTools.TypeByName`+`PropertyGetter`）経由で読み取る実装が実機で問題なく動くか
- カスタムレベル（Workshop）プレイ時の`level.ID`/`level.Root`の値の実際の安定性
- `LevelManager.Update`へのHarmony postfixパッチ、および`SaveLube.SaveCombinedSaveFile`/`DeleteSaves`/`ProgramStartInitialize`へのリフレクション経由Harmonyパッチが実機で問題なく動くか
- `language.MENUFACTORY_OBJECTIVE`の実際の文字列内容（埋め込みリソースのため未確認。実機で目視確認すれば十分）
