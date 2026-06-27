# Pause Location Display 調査メモ

逆コンパイル（ilspycmd, `JumpKing.exe` v実機インストール済みのもの）による調査の経緯と、実現可能性の結論をまとめたもの。

**実装状況**: 本実装はこの調査結果に基づいて完了し、ビルドも通っている（リポジトリルートの各`.cs`ファイル）。実機での1回目のテストで以下2点の指摘があり対応済み: (1) ポーズ画面下部は「Objective」の下に追加するのではなく置き換える表示にした、(2) `(n回目)`のような日本語はゲームのフォントが非対応で`#`に化けるため英数字表記`(xn)`に変更した。さらに(3) 隣接エリアが境界スクリーンを共有する実データ（下記「訂正・追記」参照）が原因のエリア判定ミスも発見・修正済み。下記「未検証事項」のうち、この回で確認できた項目以外は引き続き未検証。

**仕様変更（実機テスト後）**: パターンB（すき間・通り道）の表示を、直前のエリア名・枚数・挑戦回数を引き継ぐ方式から、固定の汎用テキスト`"On the way..."`のみを表示する方式に変更した（「確定した仕様」節のパターンBの説明は古い。`AreaTracker.GetDisplayText()`参照）。また、メインメニューだけでなくポーズメニューからも設定（Enabled・総数表示パターン・挑戦回数カウンター）を変更できるようにした（`[PauseMenuItemSetting]`）。

## 用語: 「画面番号」の定義

このドキュメント・実装全体で出てくる`Location.start`/`end`/`unlock`、`Camera.CurrentScreenIndex1`、コード中の`screenIndex1`はすべて同じ数値体系を指す。これを**「画面番号」**と呼ぶことにする。

**定義**: レベル全体の1枚の縦長テクスチャを360ピクセル単位で輪切りにしたときの、各輪切りに振られる1始まりの連番。`JumpKing.Camera`の実装（`UpdateCameraWithVelocity`内の`num = -(int)Math.Floor(p_center.Y / 360f)`）でプレイヤーのY座標から直接算出される。「テクスチャの読み込み順」ではなく「プレイヤーの今いる高さがどの輪切りに当たるか」が本質。ワープが絡まない限り、プレイヤーが登るほど画面番号は大きくなる（このmod全体の前提のひとつ）。

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

### 訂正・追記: 実機データでは隣接エリアが境界スクリーンを共有しており、(3)に複数マッチし得る

実機の`Content/gui/location_settings.xml`を確認したところ、連続する2エリアは**前のエリアの`end`と次のエリアの`start`が同じ値**になるよう意図的に作られている（例: `REDCROWN_WOODS`(`start=1,end=6`) → `COLOSSAL_DRAIN`(`start=6,end=10`)）。つまり境界のスクリーン（上記の例では6）は両方の`Location`に同時にマッチし、(3)の判定だけでは一意に決まらない。

**訂正（再度）**: 「境界スクリーンは常に後ろ側のエリアの`unlock`」という上記の記述は誤りだったことが、`COLOSSAL_DRAIN`→`FALSE_KINGS_KEEP`の境界（実機テストで発覚: 本来`Colossal Drain`最後の1枚であるべき画面10が`False Kings Keep 1`と誤表示された）で判明した。実際のXMLを確認すると：

| 境界スクリーン | 手前のエリア(end) | 後ろのエリア(start, unlock) |
|---|---|---|
| 6 | REDCROWN_WOODS(end=6) | COLOSSAL_DRAIN(start=6, **unlock=6**) |
| 10 | COLOSSAL_DRAIN(end=10) | FALSE_KINGS_KEEP(start=10, **unlock=11**) |

つまり`unlock == start`になっている境界（後ろ側が境界スクリーンそのもので「解禁」される）はむしろ例外で、`unlock`が`start`より1以上大きい境界（後ろ側の「解禁」はその次の画面以降）の方が多い。`JumpKing.exe`を逆コンパイルして確認した`LocationComp.CheckIfNewScreen()`（エンジン自身が「新規エリア発見」ポップアップを出す判定）は、まさにこの`unlock`フィールドを使い、`p_screenIndex1 == location.unlock`の瞬間にのみ「次のエリアに進んだ」と判定している（`start`は一切見ていない）。これに合わせ、`LocationResolver.Resolve`は「複数マッチした場合は`start`が最大のものを優先」という単純なルールをやめ、**`p_screenIndex1 >= location.unlock`を満たす候補だけを対象に、その中で`start`が最大のものを選ぶ**よう修正した（`unlock`を満たさない候補は「まだ解禁されていない」として除外され、結果的に手前のエリアが選ばれる）。これにより両方の境界例で実機の見た目と一致する。

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

## 実機テストで発覚した不具合3件と修正（このセッション）

### 不具合1: 脇道に入ってから元のエリアに戻ると、誤って「クリア済み」になる

`AreaTracker.OnUpdate`の旧実装は「現在地が`Location`の範囲内に完全マッチしていない（パターンB）かつ`screenIndex1`が直前の完全マッチエリアの`end`を超えている」ことだけを根拠に`MarkCleared`を呼んでいた。Jump Kingの画面番号（`Camera.CurrentScreenIndex1`）はプレイヤーの垂直位置（Y座標）だけから決まる純粋な数値であり、「進む方向の次のエリア」かどうかとは無関係 — ワープ（`TeleportLink`、隣接画面である必要はなく任意の画面番号に飛べる）を使う脇道・隠し部屋では、その画面番号が元のエリアの`end`より大きいか小さいかは脇道の配置次第でしかなく、「`end`を超えた＝先に進んだ」という前提が成り立たない。これが報告された不具合（REDCROWN WOODSの途中の脇道→帰還で誤クリア判定）の直接の原因。

ユーザー自身が指摘した通り「エリア名が変わったとき、それが次のエリアなのかただの脇道なのかを画面番号だけから一般的に区別する方法は無い」（`Location`データに「これは脇道」という属性自体が存在しない）。これを踏まえ、**完全に正確な判定ではなく、報告された具体的なケースを含む大半のケースで誤判定を防ぐ妥協案**として、「クリア済み」と判定する条件を「`end`を超えた」から「`start`昇順で並べたときに直後に来る、特定のエリアに実際に到達した（パターンA完全マッチで）」に変更した（`AreaTracker.FindNextLocation`+`OnUpdate`）。これにより：

- 通常の前進（間に名前の無いすき間がある場合も含む）は変わらず正しく「クリア済み」になる
- 脇道（それ自体が`Location`として定義されていない、または定義されていても「次のエリア」とは異なるもの）に入って戻ってきても、「次のエリア」に完全マッチしない限り誤ってクリア済みにはならない
- 残る限界: 脇道がワープ先の画面番号の都合で「次のエリア」自身の`[start,end]`範囲とたまたま重なってしまう場合（非常に稀な配置）は防げない。これは`Location`データだけでは原理的に区別不能なため、これ以上の対応は見送る

### 不具合2: 一番最初のエリアだけ、挑戦回数が増えない

挑戦回数のルールは「より手前の初回訪問順（`order`）のエリアから、より後の初回訪問順のエリアに入ったとき（＝下から登ってきたとき）に+1」。これは2番目以降のエリアには機能するが、一番最初のエリア（`order=0`）には「これより手前の`order`を持つエリア」が存在しないため、ルール上一度も+1されるタイミングが来ない（初回登録の1のままになる）。

ユーザー要望通り、一番最初のエリアだけ特別扱いし、「（`order`の前後関係を問わず）スタート地点に戻ってきたら+1」というルールを追加した（`AreaProgressStore.OnEnterArea`の`isFirstArea`引数）。一番最初のエリアかどうかは、`AreaTracker`が持つ`start`昇順の配列の先頭要素と`start`が一致するかで判定する。

### 不具合3: COLOSSAL DRAINとFALSE KINGS KEEPの境界がおかしい（`Colossal Drain`最後の1枚であるべき画面が`False Kings Keep 1`になる）

「訂正（再度）」の節に詳細を記載。`LocationResolver.Resolve`の境界判定ルールを「`start`が最大の候補を優先」から「`unlock`を満たす（`p_screenIndex1 >= location.unlock`）候補だけの中で`start`が最大のものを優先」に修正した。

## 実機テストで発覚した追加の不具合・要望と対応（このセッション その2）

### 接地判定の追加（`PlayerGroundChecker`）

エンジン自身の「エリア新規発見」ポップアップも`LocationComp.Update`内で`IsPlayerOnGround()`がtrueの時しか進行しない（`EntityComponent.EntityManager.instance.Find<PlayerEntity>().GetComponent<BodyComp>().IsOnGround`、いずれも`public`なので素のC#参照でリフレクション不要）。`AreaTracker.OnUpdate`もこれに合わせ、空中を素通りしただけではエリア到達・クリア判定を進めず、何らかの足場に実際に立った時だけ進行するようにした。

### ページ番号の基準を`start`から`unlock`に変更

`start < unlock`になっているエリア（例: False Kings Keep `start=10, unlock=11`）では、解禁前の画面（10）は`LocationResolver`が前のエリア側に解決するため、このエリアとして表示される最初の画面は常に`unlock`。旧実装は`current = screenIndex1 - start + 1`だったため、その最初の画面が`2/5`のように1ページ目から始まらなかった。`current`/`total`の基準を`unlock`に変更（`current = screenIndex1 - unlock + 1`、`total = end - unlock + 1`）。`unlock == start`（多数派）のケースは計算結果が変わらない。

### 枠サイズがポーズを開き直しても更新されない問題

`DisplayFrame.Initialize()`（`CalculateBounds`含む）は一度しか呼ばれず、`PauseManager`自体もレベル中1回しか構築されない（＝`MenuFactoryPatches.CreatePauseInfoPrefix`も1回しか走らない）ため、枠のサイズはその瞬間のテキスト長で固定されたままになり、その後テキストの長さが変わっても追従しなかった。`PauseManager.SetPause(bool)`（`internal`、リフレクション経由）にpostfixを当て、ポーズに入るたび（`p_pause == true`）に同じ`DisplayFrame`の`Initialize()`を再実行するようにした（`MenuFactoryPatches.s_displayFrame`/`SetPausePostfix`）。

### テキストの中央揃え

枠サイズの計測時（`GetSize()`）だけ`MeasureBuffer`("12345")を足してテキストより少し広めに測っているが、描画時（`Draw()`）は実際のテキストだけを描画し、かつエンジンの`GuiFormat.DrawMenuItems`は常に左詰めで描く。その結果、枠の右側に`MeasureBuffer`分の余白が残っていた。`Draw()`側で`MeasureBuffer`の測定幅の半分だけ描画位置を右にずらし、見た目上中央に来るようにした（`AreaInfoTextInfo.Draw`）。

### 枠の余白(padding)を本家相当に縮小

以前「主分の半分+1」という本家`CreatePauseInfo`の`all_padding`計算を、短いテキスト用にあえて使わず`all_padding`をそのまま(16)使う変更をしていたが、実機で余白が大きすぎると判明。本家と同じ`all_padding / 2 + 1`（16→9）に戻した（`MenuFactoryPatches.CreatePauseInfoPrefix`）。

### 表記の調整

- 総数非表示時の表記を`エリア名 n`から`エリア名 n/x`に変更（総数が分からないことを`x`で明示）
- 挑戦回数の表記を`(xn)`から`(#n)`に変更（ユーザー希望、短く読みやすい記法）

### 新機能: 到達済みエリア一覧（後述の「Progression Detail」に発展・改名）

ポーズ画面のmod設定一覧に「Area History」というON/OFFトグルを追加した（`ITextToggle`ベース）。ONにすると、通常のエリア情報表示の代わりに、そのプレイで一度以上到達した全エリアを初回訪問順(`order`)に列挙し、各行に挑戦回数を付けて表示する。表示モード切替で行数・幅が大きく変わるため、トグルを押した瞬間に`MenuFactoryPatches.RefreshDisplayFrame()`で枠サイズを再計算している（ポーズの開閉イベントを介さない、メニュー操作中の即時リフレッシュが必要なケース）。**この機能は後のセッションで「Progression Detail」に発展・改名された（下記参照）。**

## 実機テストで発覚した不具合2件の修正（このセッション その3）

### 不具合: ゲームを再起動すると挑戦回数・突破済み判定が消える（Save&Exit→Continueでは保持されるのに）

`SaveLube.ProgramStartInitialize()`へのHarmonyパッチ（`AreaProgressStore.Load`を呼ぶ想定だったもの）が、実機では一度も実際の呼び出しを捕まえられていなかったことが逆コンパイルで判明した。`Program.Run()`の起動シーケンスでは、`saveManager.ProgramStartInitialize()`（`SaveLube.ProgramStartInitialize()`を内部で呼ぶ）が`new Game1()`より**前**に1回だけ呼ばれる。一方、modのHarmonyパッチは`JumpGame`のコンストラクタ内（`ModLoader.Instance.CallBeforeLevelLoadMethods()`）で適用されるため、`Game1`より後に存在する`JumpGame`が構築されるまでパッチは効かない。つまり、唯一の本物の呼び出しが完了した**後**にパッチが取り付けられる形になり、`AreaProgressStore.Load()`は実機では永久に呼ばれていなかった。

同一プロセス内の「Save&Exit→Continue」が正しく動いていたのは、メモリ上の`s_levels`がそもそも消えていなかった（リロードする必要が無かった）だけで、ゲーム自体を再起動（プロセス再生成）すると、ロードされない空の状態からやり直しになっていた。

`SaveLubePatches`から効かない`ProgramStartInitialize`パッチを削除し、代わりに`ModEntry.BeforeLevelLoad()`（mod自身が1回だけ、`LevelManager.LoadScreens()`より前に呼ばれる）から直接`AreaProgressStore.Load()`を呼ぶように変更した（`SaveLubePatches.LoadProgress()`）。

### 不具合: 挑戦回数の加算方向が逆（落ちると増え、登っても増えない）→ 一度`start`比較に修正 → ワープを踏まえ`Order`比較に戻した（最終形）

旧ロジック（`AreaProgressStore.OnEnterArea`）は、エリアの初回訪問順`Order`（時系列順）を使い、「前のエリアより`Order`が大きいエリアに入った＝登った」と判定していたが、不具合が報告された。その場でエリアの`start`（画面番号、空間的な位置）を直接比較する方式（`newStart > previousStart`）に修正し、ユーザーに動作確認してもらった。

その後、別の機能（Personal Best）の検討中に、**このゲームにはワープが存在するため、画面番号だけでは「本当にそこまで進んだか」を保証できない**（ワープで本来の進行と無関係に大きい画面番号へ飛ばされる可能性がある）ことが分かった。ユーザーから明示的な確認を得た正しい前提は逆: **`AreaEntry.Order`（各エリアに初めて到達した実際の訪問順）は、ワープが絡んでも基本的に正しい進行順とみなせる。** 画面番号の大小ではなく、実際にプレイヤーが訪れた順序そのものを見ているため。

これを踏まえ、ユーザーの指示により挑戦回数の加算方向判定も`Order`比較に戻した（最終形）: 前のエリアより`Order`が大きい（＝より後で初めて訪れた）エリアに入った時だけ、そのエリアの挑戦回数を加算する（`previousEntry.Order < newEntry.Order`）。一番最初のエリアの例外（`isFirstScreenOfFirstArea`: 一番最初の画面=`screenIndex1 == 最初のエリアのstart`に落ちたら無条件で加算）はそのまま維持。Personal Best（PB）判定も同じ理由で`Order`の最大値でエリアを選ぶ方式にしている（下記参照）。

## 新機能: Personal Best表示・Progression Detail（このセッション その4・5・6）

### Personal Best（PB）

そのプレイでこれまでに到達した中で一番進んだ場所を、エリア名だけでなく**そのエリアの何枚目か**まで含めて表示する機能（例: Colossal Drainの4枚目までしか到達していなければ「Colossal Drain 4」、5枚目には未到達）。

実装は2段階に分かれている:
- **PBの「エリア」**: `AreaProgressStore.GetPersonalBest`が`Order`（初回訪問順）が最大のエリアを選ぶ。上記の通り、ワープが絡んでも訪問順そのものは信用できるため。
- **PBの「ページ番号」**: エリアごとに「そのエリア内で到達した実画面番号(`screenIndex1`)の最大値」を保持する（`AreaEntry.BestScreenIndex`、レベル単位ではなく**エリア単位**。XMLには`Area`要素の`bestScreenIndex`属性で永続化）。更新は`AreaTracker.OnUpdate`/`OnLevelStart`が完全マッチ（パターンA）の時だけ、かつ`OnEnterArea`でエリア登録済みであることを確認した後に呼ぶ（`AreaProgressStore.UpdateBestProgress(levelKey, areaStart, screenIndex1)`）。「on the way」（すき間）は完全マッチ時のみ更新するため自然に無視される。

表示時は、PBエリアの`BestScreenIndex`を使ってページ番号を計算する（`AreaTracker.GetPersonalBestText`）。

`PersonalBestToggle`（ON/OFFトグル、メイン・ポーズ両メニューに配置）でON/OFFを切り替えられる。ONの場合、現在のエリア情報（または空文字・"On the way..."）の下にもう1行「PB: エリア名 ページ番号」を追加する（`AreaTracker.AppendPersonalBest`）。

### ラップ機能とProgression Detail

各エリアに**初めて到達した瞬間のプレイ時間**（`AchievementManager.instance.GetCurrentStats().timeSpan`、ゲーム自身のセッション統計と同じ値）を「ラップタイム」として記録する機能。`AchievementManager`は`internal`なため、`PlayTimeAccessor`が`AccessTools`経由でリフレクションする（`instance`フィールド＋`GetCurrentStats()`メソッド。戻り値の`PlayerStats`構造体自体は`public`なのでそのままキャストできる）。記録は`AreaProgressStore.AreaEntry`に`LapTime`（`TimeSpan`、XMLには`Ticks`で永続化）として保存され、そのエリアの初回登録時（`OnEnterArea`の新規エリア分岐）にのみ設定される（再訪問では上書きしない）。

このラップタイムは通常のポーズ画面には表示せず、既存の「Area History」を「Progression Detail」に発展・改名したトグル（`ProgressionDetailToggle`）をONにした時だけ表示される。表示形式は先頭に「pb: エリア名 ページ番号」の行（上記PBと同じ計算）、続けて各エリアを**最後に訪れたものから順に**（最初のエリアが一番下になるように）「エリア名 (#挑戦回数) ラップタイム」で列挙する（`AreaTracker.GetProgressionDetailText`）。エリア単位の行ではPBを個別にマークする必要が無くなったため、以前あった` <- PB`の表示は廃止した。

## 不具合: 一度も入っていないはずのエリアがProgression Detailに出現する（カスタムレベル `3315431380` で発覚）

実機テストで報告: 画面番号が`1->2->142`と進み、142は通常表示で`On the way...`（パターンB・すき間）だったにもかかわらず、Progression Detailには無関係な`The Tower of Frozen Genesis`というエリアが表示された。

原因は`LocationResolver.Resolve`のパターンBフォールバックの仕様にあった。パターンBは「`p_screenIndex1`以下の`start`を持つ`Location`の中で`start`が最大のもの」を選ぶが、これは**配列内の全`Location`が対象**であり、プレイヤーが実際に通った経路上のエリアである保証は無い。バニラのように全エリアが連続的に並んでいるデータでは「直前に完全マッチしていたエリア」が自然に選ばれるが、カスタムレベルでは、現在地と無関係な（経路上に存在しない）エリアの`start`がたまたま「142以下で最大」になっているだけで選ばれてしまうことがある。

`AreaTracker.OnUpdate`/`OnLevelStart`は、`resolved.Area`（パターンA・B問わず）が変わるたびに`AreaProgressStore.OnEnterArea`（初回訪問登録・挑戦回数）を呼んでいたため、このパターンBの「無関係な best-guess」エリアまで誤って「訪問した」として記録してしまい、それがProgression Detailの一覧に混入していた。通常表示側は`!resolved.IsExactMatch`を見て`On the way...`に差し替えていたため症状が出ていなかったが、裏の記録処理は完全マッチかどうかを見ていなかった。

**修正**: `OnEnterArea`・`UpdateBestProgress`・クリア済み判定を、`resolved.IsExactMatch`が`true`の場合だけ呼ぶように統一した（パターンBの間は記録処理を一切行わない）。これに伴い、それまで「直前に解決されたエリア（パターンA・B問わず）」を覚えていた`s_lastResolvedStart`は不要になり削除し、`s_lastExactArea`（完全マッチのみを覚える）一本に統一した。

**既知の制限**: この修正は今後の誤登録を防ぐだけで、修正前に既に誤って記録されてしまった`AreaProgressStore`のXMLエントリ（例: 上記`The Tower of Frozen Genesis`）は自動的には削除されない。該当レベルで一度「Restart」する（進捗データがクリアされる）か、`F.AreaInfoDisplayOnPause.AreaProgress.xml`から該当エントリを手動で削除する必要がある。

## Enabledトグルの廃止（mod全体は常に有効）

ポーズ中に**Enabled**をOFF→ONと切り替えると、表示・追跡が「変な感じ」になる不具合が報告された。原因は、OFFの間`LevelManagerPatches.UpdatePostfix`が`AreaTracker.OnUpdate()`自体を呼ばなくしていたため、プレイヤーが進んでいる間も挑戦回数・突破済み・PB・ラップタイムの追跡が完全に止まっていたこと。その後ONに戻すと、`s_lastExactArea`等の内部状態が「OFFになった時点」のまま止まっており、実際の現在地との間に「OFFだった間に通過した分」のギャップが生じ、次のエリア遷移検出が不整合な値を見ることになっていた（例: 本来の挑戦回数の加算/見送り判定が誤る、クリア済み判定が正しい「直後のエリア」を見られない等）。

一度「常時ライブ判定で即時反映できるようにする」という改修（`AreaTracker.GetDisplayText`が`Settings.IsEnabled`をその場で見て本家Objectiveテキストにフォールバックする、`ModEntry.OnLevelStart`を常時実行する等）を試したが、結局「追跡処理そのもの」を一時停止できる以上、この不整合は構造的に避けられないと判断し、**Enabledトグル自体を完全に廃止**することにした（ユーザー判断）。これにより:

- mod全体は常に有効。無効化する手段は無い
- `EnabledToggle.cs`を削除し、`Settings.IsEnabled`・`[MainMenuItemSetting] MainEnabledSetting`・`[PauseMenuItemSetting] PauseEnabledSetting`を全て削除
- `LevelManagerPatches.UpdatePostfix`は常に`AreaTracker.OnUpdate()`を呼ぶ
- `MenuFactoryPatches.CreatePauseInfoPrefix`は常にこのmod独自の表示枠を設置する（本家Objectiveへのフォールバック分岐は不要になった）
- 各トグル（`AttemptCounterToggle`/`PersonalBestToggle`/`TotalDisplayModeOption`）の`CanChange()`から`Settings.IsEnabled`チェックを削除（`ProgressionDetailToggle`は他に条件が無くなったため`CanChange`のオーバーライド自体を削除し、基底の既定値`true`に委ねている）

このため、上記「追加要望3」「確定したデフォルト値」節などに残る「Enabled」に関する記述は、当時の設計判断の記録としてそのまま残すが、現在の実装はこの節の内容で上書きされている。

## 既知の仕様: ワープで2つ以上先のエリアに飛ぶと、ワープ元のエリアが「突破済み」にならない

実機確認: `Redcrown Woods`の2枚目からワープで`Philosopher's Forest`（定義順でかなり後ろのエリア）に直接入った場合、`Redcrown Woods`は「突破済み」にならない。

原因は「脇道に入って戻ってきただけでは誤って突破済みにならない」ための既存の判定ロジックそのもの（`AreaTracker.OnUpdate`、`FindNextLocation`で「直前のエリアの、定義順で本当に1つ後ろのLocation」を求め、新しく入ったエリアの`start`がそれと一致した時だけ`MarkCleared`を呼ぶ）。ワープ先が定義順で直後のエリア（例: `Redcrown Woods`→`Colossal Drain`）ならこの判定は正しく成立して突破済みになるが、2つ以上先のエリアへワープした場合は「直後のLocation」と一致しないため成立しない。

ここで言う「定義順」「隣接」「2つ以上先」は、すべて`s_sortedLocations`の並び＝**画面番号(`start`)の昇順**を指す（`AreaTracker.OnLevelStart`の`Array.Sort(locations, (a, b) => a.start.CompareTo(b.start))`）。`AreaEntry.Order`（初回訪問順）とは無関係で、`FindNextLocation`はOrderを一切参照しない。

**これは意図した・許容するトレードオフであり、修正不要**（ユーザー確認済み）。脇道の誤判定を防ぐための仕組みを、ワープにも例外なく適用しているだけであり、個別に特別扱いするとその分だけ「本当に脇道なのか、ワープで複数エリア飛ばしたのか」を区別する必要が生じ複雑化する。`TEST_CASES.md`の「11. 既知の限界の確認」に明記。

## 新機能: Babe（エンディング）画面の特別表示

3つの「Babe」エンディング画面（バニラ: `Content/settings/babe.xml`相当の42/99/153画面、カスタムレベル: `level_settings.xml`の`ending_screen`/`ending_screen_second`/`ending_screen_third`）にちょうどいる間だけ、通常のエリア表示を上書きして`Babe`と表示する機能を追加した。3つの画面は区別しない（どれも同じ`Babe`という表示になる）。

**画面番号の取得元**: 自前でXMLを読まず、ゲーム自身が内部的に解決済みの値を使う。`JumpKing.GameManager.MultiEnding.EndingManager`（`internal`クラス。`public static instance`プロパティ、`public int[] GetWinScreens0()`メソッド）が、`NormalEnding`/`NewBabePlusEnding`/`OwlEnding`の3つの`ENDING_SCREEN0`（バニラはハードコード、カスタムレベルは`level_settings.xml`を読んで`-1`した0始まりの値、`Camera.CurrentScreen`と同じ数値体系）を配列で返す。これに`+1`すれば、このmodの`screenIndex1`（`Camera.CurrentScreenIndex1`、`Location.start`等と同じ1始まりの体系）に揃う。`PlayTimeAccessor`と同じ要領で`AccessTools`経由のリフレクションで読む（`EndingScreensAccessor.cs`）。`EndingManager.instance`は`GameLoop.OnPreGameStart`で毎レベル(再)生成されるため、`AreaTracker.OnLevelStart`で毎回取得し直す。

**仕様（ユーザー判断・確認済み）**:
- **表示だけの上書き、`AreaProgressStore`には一切影響しない**: Babe画面はLocation一覧（本筋・脇道）の範囲内に重なって存在する（例: 本筋エリアの途中の1画面）ことが多いが、独立したエリアとして登録する設計にはしなかった。`Order`・挑戦回数・突破済み・`BestScreenIndex`は、その画面が属する本来のエリアとして裏側では変わらず追跡され続ける。`GetCurrentAreaDisplayText`が返すテキストだけを、その画面にいる間だけ`Babe`に差し替える（`AreaTracker.GetBabeDisplayText`）
- **Babeを最優先で上書き**: 通常のLocation解決（パターンA/B）より先に判定するため、Babe画面がどのLocationの範囲内にあっても関係なく`Babe`が出る。1画面分だけの上書きなので、その画面を離れた瞬間（Babeのあとに先に進む、あるいは下に落ちる）通常表示に戻る
- **PB行・Progression Detailの`pb:`行も同様に上書きするが、こちらは「現在その画面にいる間だけ」ではなく永続的（実機テストで修正）**: 最初は現在地表示と同じく「今その画面にいる間だけ」`PB: Babe`を出す実装にしたが、実機で「Babeに到達した後、そこから落ちるとPBが本来のエリア（例: `The Tower 4`）に戻ってしまう」という指摘を受けた。Babeのエンディング画面に到達することはそのプレイで最も深い到達点であるはずなので、一度到達したら**そのプレイ中はずっとPBが`Babe`のまま**になるのが正しい仕様だと判断した。
  - `LevelEntry`に`HasReachedBabe`（`bool`）を追加し、XMLの`<Level reachedBabe="...">`属性として永続化（`Order`等と同じレベル単位のデータ）。一度`true`になったら、そのレベルの進捗データがクリアされない限り（`Restart`等）`false`に戻らない
  - `AreaProgressStore.MarkBabeReached(levelKey)`/`HasReachedBabe(levelKey)`を追加。`AreaTracker.OnUpdate`・`OnLevelStart`の両方で、画面がBabeに一致した時点で`MarkBabeReached`を呼ぶ（`OnLevelStart`側はセーブがBabe画面の上でちょうど再開された場合のカバー用）
  - `AppendPersonalBest`・`GetProgressionDetailText`の`pb:`行は、`HasReachedBabe(levelKey)`が`true`なら無条件で`"Babe"`を返す（`GetPersonalBestText()`を呼ばない）。現在地表示（`GetCurrentAreaDisplayText`）の方は元のまま「今その画面にいる間だけ」で変更無し（現在地は常に実際の現在位置を正しく反映すべきなので、両者は意図的に非対称）
- **Babeに到達したら、その画面が属する本来のエリアも「突破済み」扱いにする（追加要望）**: 例えば`main babe`で`The Tower`エリア4枚目にBabeがいる場合、Babe到達と同時に`The Tower`自体も突破済みになる。`AreaTracker.OnUpdate`/`OnLevelStart`で、Babe画面に一致した時点の`resolved.Area`（exact matchの場合のみ）に対して`AreaProgressStore.MarkCleared`を呼ぶ。
  - 呼ぶタイミングに注意が必要だった: その時点でそのエリアがまだ一度も登録されていない場合（`AreaProgressStore.MarkCleared`は未登録のエリアに対しては何もしない一方通行のno-op）、先に`OnEnterArea`によるエリア登録が完了してから呼ばないと取りこぼす。`OnUpdate`では関数の最後（`UpdateBestProgress`の後）、`OnLevelStart`では`OnEnterArea`呼び出しの直後に、それぞれ`MarkCleared`を移動して対処した

## Mods設定画面の枠サイズ不具合の根本修正（パディング方式から廃止）

`Show Total`オプション（`TotalDisplayModeOption`）の表示テキストが`Always`/`Never`/`After Clear`で長さが違うため、Mods設定画面（メイン・ポーズ両メニュー）をある値で開いた状態のまま、その場で別の値に切り替えると、枠のサイズが追従せずテキストがはみ出す不具合があった。

最初はテキストを固定長にパディングする方式で対処したが、どこにパディングを入れても「ラベルと値の間」または「値と`>`の間」のどちらかに不自然な隙間が残ってしまい、ユーザーから2度にわたって指摘された。

逆コンパイルで調査した結果、根本原因が判明した。`JumpKing.PauseMenu.MenuFactory.CreateModOptions`（`private`メソッド）が、modごとの設定項目をまとめた`MenuSelector`を生成する際、`MenuSelector.Initialize()`（`m_format.CalculateBounds(m_menu_items)`で全項目の現在のテキストを測り、枠サイズを計算する`public`メソッド）を**ポップアップに入った時に1回だけ**呼んでいる。ポップアップを開いたままオプションの値をその場で変えても、`Initialize()`は再実行されないため、枠サイズが古いテキストの長さのまま固定されてしまう。

**修正**: パディングをすべて削除し、根本原因に対処した。
- `MenuFactoryPatches.cs`に`CreateModOptions`へのHarmonyポストフィックスを追加。引数の`ModAssembly[] assemblies`を見て、このmod自身（`typeof(ModEntry).Assembly`と一致するもの）の設定項目を含む呼び出しだけを判別し、その時の戻り値（`MenuSelector`）を`s_modSettingsMenu`に保持する
- 新しい`public static void RefreshModSettingsMenu()`が`s_modSettingsMenu?.Initialize()`を呼ぶ。これを`TotalDisplayModeOption.OnOptionChange`から呼ぶことで、値が変わった瞬間に枠サイズが正しく再計算される
- これは既存の`RefreshDisplayFrame`（ポーズ画面下部の自前の表示枠用）と全く同じ発想の修正で、対象が「自前のDisplayFrame」ではなく「ゲーム本体のMods設定ポップアップのMenuSelector」という違いだけ
- `AttemptCounterToggle`/`PersonalBestToggle`/`ProgressionDetailToggle`は項目名のテキスト自体が変わらない（チェックボックスの記号だけが変わる）ため、この対応は不要

### 追加修正: `After Clear`選択時、わずかにテキストが枠からはみ出す

上記の根本修正後も、`After Clear`（3つの選択肢の中で最長）が選択された状態で開いた・切り替えた直後、ごくわずかに右端の`>`がはみ出す現象が残った。`IOptions`を逆コンパイルして原因を特定した。

- 枠サイズの計算（`GuiFormat.CalculateBounds`）は各項目の`IMenuItem.GetSize()`を使う。`IOptions.GetSize()`は`MenuItemHelper.GetSize(text)`（内部は`SpriteFont.MeasureString(text).ToPoint()`）に矢印分の幅(`GetExtraWidth()`)を加算したもの
- `Vector2.ToPoint()`（MonoGame）はX/Yを**0方向への切り捨て**で`int`化するため、`MeasureString`が返す浮動小数点の幅よりも`GetSize()`の戻り値が最大1px弱小さくなりうる。これが、本来ぴったり収まるはずの最長テキストでもわずかに不足する原因
- 一方、実際の描画位置を決める`IOptions.MyDraw`は`GetSize()`を一切使わず、`m_font.MeasureString(text)`を**その場で独自に再計算**して矢印・テキストの位置を決めている

つまり「枠の计算に使う数値」と「実際に描画される位置」は完全に別経路であり、**`GetSize()`だけを大きめに返すよう上書きしても、見た目の文字・矢印の配置（隙間の有無）には一切影響しない**。これを利用し、`TotalDisplayModeOption`で`GetSize()`をオーバーライドし、`base.GetSize()`の戻り値に固定で`+6px`した値を返すようにした。テキストへのパディング（文字を増やす）とは違い、見た目の隙間を一切生まずに枠だけ少し大きく確保できる。

### 追加修正: `RefreshModSettingsMenu`が効いていなかった（メイン・ポーズ両方が同じ変数を共有していた）

上記の根本修正・追加修正を入れても、実機では「`After Clear`で開いた状態ならぴったり収まるが、`Always`等で開いてから`After Clear`に切り替えると、依然はみ出る」という報告を受けた。つまり`RefreshModSettingsMenu`が呼ばれていない（または効いていない）ケースが残っていた。

原因: メイン・ポーズ両方の「Mods」設定ポップアップは、実際にその画面へナビゲートする**前**に、メニュー全体の構築時にあらかじめ`CreateModOptions`が呼ばれて`MenuSelector`が作られている（遅延生成ではない）。このmod自身の設定は両方のメニューに登録しているため、`CreateModOptions`はこのmod用に**2回**呼ばれる（メイン用・ポーズ用）。`CreateModOptionsPostfix`が1つの`s_modSettingsMenu`変数だけに保持していたため、後から呼ばれた方（実際に画面に出ているのとは別の方）で上書きされてしまい、`RefreshModSettingsMenu`は見えていない方の`MenuSelector`だけを再計算する、という状態になっていた。

**修正（試行）**: `CreateModOptions`の`is_pause_menu`引数も受け取り、メイン用・ポーズ用を別々の変数（`s_mainMenuSettingsMenu`/`s_pauseMenuSettingsMenu`）に保持。`RefreshModSettingsMenu`は両方の`Initialize()`を呼ぶ（開いていない方を再計算しても描画されていないので無害）、という想定だった。

**この修正は取り消した**: 実機でポーズ画面側の設定ポップアップの表示自体が壊れる（枠・項目がおかしくなる）という、別の不具合が新たに発生したため、ユーザー指示によりこの変更だけを取り消し、`s_modSettingsMenu`を1つの変数に戻した（つまり「`Always`等で開いてから`After Clear`に切り替えるとはみ出る」問題自体は未解決のまま残っている）。原因の特定・再修正は今後の課題。

### 全面的に取り消し: `CreateModOptions`へのHarmonyパッチ自体を撤去

1つ前の修正を取り消した後も、ポーズ画面側の設定ポップアップの表示崩れが直らないという報告を受けた。つまり問題は「メイン/ポーズの変数を分けたこと」ではなく、もっと根本的に**`MenuFactory.CreateModOptions`へのHarmonyパッチそのもの**（全modの設定ポップアップ生成で共有されている`private`メソッドへの直接パッチ）が原因だった可能性が高いと判断し、ユーザー指示によりこの一連の「根本修正」全体（`CreateModOptionsPostfix`・`RefreshModSettingsMenu`・`s_modSettingsMenu`フィールド、および`TotalDisplayModeOption`側の`GetSize()`オーバーライド・`OnOptionChange`からの`RefreshModSettingsMenu`呼び出し）を完全に削除し、`TotalDisplayModeOption`を**`CreateModOptions`パッチ導入前の状態（`PadLeft`によるパディング方式）**まで戻した。

**現状**: 「設定値を`Always`等で開いた状態のまま、その場で`After Clear`に切り替えるとテキストがわずかにはみ出る」問題自体は再び未解決に戻っている。今後この問題に再度取り組む場合は、`MenuFactory`内部の共有メソッド（`CreateModOptions`等）への直接パッチは、他modの設定画面にも影響しうる高リスクな手段だったと分かったので、別のアプローチ（例: パディング量の微調整のみで妥協する等）を検討すること。

### 再度の方針変更: パディングも撤去し、プレーンなテキストに戻す

ユーザーから「`Show Total:`と`Never`の間に入っているスペースを削除してほしい」という明示的な指示があり、`PadLeft`によるパディングも完全に削除し、`CurrentOptionName()`を`"Show Total: " + 値`のプレーンな文字列を返すだけに戻した（パッチ導入前の最初の状態と同じ）。

これにより「Total:」と値の間の見た目の隙間は完全に無くなるが、3つの選択肢の長さが再び異なるため、短い選択肢（`Never`等）でポップアップを開いた状態のまま`After Clear`に切り替えると、テキストが枠からはみ出る現象は再発する（枠サイズはポップアップを開いた瞬間の1回だけ計算されるため）。これは見た目の隙間を完全に無くすこととのトレードオフとして、ユーザーが明示的に選んだ結果。

## 突破済み判定の追加: ワープでも、Order順で見て本当に次のエリアなら突破済みにする

`FindNextLocation`ベースの既存の突破済み判定は、`s_sortedLocations`（`start`の昇順）で見て直後のLocationにしか反応しない。一方、ワープの行き先画面番号は`TeleportLink`が保持する任意の値であり、`start`の大小関係と無関係（「ワープ先のエリア」の節で確認済み）。そのため、**ワープで実際に「次のエリア」へ進んでも、その行き先の`start`が配列上の直後でなければ、既存の判定は反応しない**という抜けがユーザーから指摘された。

`Order`（初回訪問順）はワープが絡んでも実際の訪問順序を正しく反映するため、「直前のエリアの`Order`+1にあたるエリアに到達した」を素直に追加条件にすればよさそうに見えるが、これは脇道に**初めて**入った瞬間にも成立してしまう（脇道もこのプレイで新規発見した「直後のエリア」には違いないため）。これにより「脇道に入っただけで本筋が誤って突破済みになる」という、既存のテストケースが守っていたはずの保証を壊してしまう。

**採用した条件（ユーザー提案）**: 「そのエリアの`end`（最後の画面）に**一度以上到達していること**」を追加の前提条件にする。脇道は通常そのエリアの`end`より手前で分岐するため、`end`に到達する前に脇道へ入った場合はこの条件を満たさず、誤判定にならない。

**実装**: `AreaProgressStore.MarkClearedIfOrderSequential(levelKey, previousStart, previousAreaEnd, newStart)`を新設。
- 既存の`BestScreenIndex`（そのエリア内で到達した最深画面）を使い、新しいフィールドを増やさずに「`end`に到達済みか」を`BestScreenIndex >= previousAreaEnd`で判定
- `previousEntry.Order + 1 == newEntry.Order`で「実際の訪問順で直後か」を判定
- 両方満たした場合のみ`HasFullyCleared = true`にする

`AreaTracker.OnUpdate`の既存の`FindNextLocation`チェックのすぐ後、かつ`OnEnterArea`の**後**（新規エリアの場合、`Order`が確定するのが`OnEnterArea`内のため）に呼ぶ。既存の`FindNextLocation`チェックとは独立した、追加の経路として並存する（どちらかが成立すれば突破済みになる）。

処理負荷は、既存データ（`BestScreenIndex`・`Order`）の整数比較のみで、ループや追加の走査は発生しないため無視できるレベル（ユーザーへの説明済み）。

`TEST_CASES.md`5節に、実際に確認すべき項目として追加した（BoE: Intense heat wind hell→doodlebugのワープで確認予定）。
