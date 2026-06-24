# AreaInfoDisplayOnPause

Jump Kingのmodです。ポーズ画面の「Objective」欄の下に、現在いるエリアの名前・そのエリアの何枚目にいるか・そのエリアへの挑戦回数を表示します。あわせて、これまでの最高到達地点（Personal Best）や、エリアごとの詳細な進行記録（Progression Detail）も表示できます。

表示例: `Icy Caverns 3/5 (#2)` / `PB: Colossal Drain 4`

- エリアの境界はゲーム本体の`gui/location_settings.xml`（バニラ・カスタムレベル共通の仕組み）をそのまま使うため、追加データなしでバニラ・対応済みカスタムレベルの両方で動作します
- エリア定義の範囲外（次のエリアへ向かう途中の「すき間」）にいる場合は`On the way...`と表示されます
- エリアの総数（`n/m`の`m`）を表示するかどうかは3パターンから選べます（「常に表示」「表示しない」「そのエリアを突破した後だけ表示」）
- 挑戦回数はそのプレイ（セーブ）中に各エリアへ初めて入った順番を実測し、より後で初めて訪れたエリアに登るたびに加算されます
- **Personal Best**: そのプレイでこれまでに到達した中で一番進んだ場所を、エリア名とそのエリアの何枚目かまで表示します（例: `Colossal Drain 4`）
- **Progression Detail**: 通常表示の代わりに、到達済みの全エリアを最後に訪れたものから順に列挙し、各エリアの挑戦回数とそのエリアに初めて到達した時のプレイ時間を表示します

詳しい調査経緯・仕様検討の過程は[NOTES.md](NOTES.md)を参照してください。

## 使い方

メインメニュー（ホーム画面）→ **Mods** → **AreaInfoDisplayOnPause**、またはゲーム中の**ポーズメニュー → Mods → AreaInfoDisplayOnPause**から、以下の設定を変更できます。

1. **Show Total** - 総数表示のパターン
2. **Attempt Counter** - 挑戦回数表示のON/OFF
3. **Personal Best** - 最高到達地点表示のON/OFF
4. **Progression Detail** - 通常表示の代わりにエリアごとの詳細記録を表示するON/OFF（ONの間は2・3の設定は意味を持たないためグレーアウトされます）

mod全体を無効化するEnabledトグルは無い（常に有効）。プレイ中に追跡を止めると、再度有効化した時に直前までの空白期間によって挑戦回数・突破済み判定などの記録が不整合になるため、意図的に廃止した。

設定・進捗データはmod自身のdllと同じフォルダに、それぞれ`F.AreaInfoDisplayOnPause.Settings.xml`・`F.AreaInfoDisplayOnPause.AreaProgress.xml`として保存されます。進捗データはゲーム本体のセーブ・ロード・削除と同期します。

## 動作タイミングと負荷

このmodがいつ動き、どのくらいの負荷がかかるかをまとめる。

- **ポーズ中以外、毎フレーム**（`LevelManager.Update`へのpostfix、`AreaTracker.OnUpdate`）: 現在地の判定・挑戦回数/突破済み/最高到達地点の更新を行う。プレイヤーが足場に立っていない（空中にいる）フレームは即座に抜けるため何もしない。立っている場合も、エリア一覧（バニラで26件程度）を線形走査するだけの軽い処理で、ゲーム本体が同じ`LevelManager.Update`内で毎フレーム行っている当たり判定（1画面分のブロック配列との衝突チェック）と比べて桁違いに軽い。エリアが実際に変わった時だけ進捗データの読み書き（後述のロック・プレイ時間取得を含む）が発生し、変わらないフレームではほぼ何もしない
- **ポーズ中のみ、毎フレーム**（`AreaInfoTextInfo`の`Draw`/`GetSize`、`AreaTracker.GetDisplayText`）: 表示テキストの再計算。ポーズ中は上記の毎フレーム更新が走らないため、両者が同時に動くことはない
- **ポーズを開いた瞬間**（`PauseManager.SetPause`へのpostfix）、**および設定をトグルした瞬間**: 表示枠のサイズ再計算（`DisplayFrame.Initialize()`の再実行）。テキストの長さに合わせて枠を測り直すだけの軽い処理
- **ゲーム起動時に1回だけ**（`ModEntry.BeforeLevelLoad`）: 進捗データのXMLファイルをディスクから読み込む
- **約1秒ごと、ゲーム本体の自動保存と同じタイミング**（`SaveLube.SaveCombinedSaveFile`へのpostfix）: 進捗データをXMLファイルに書き出す。この処理はゲーム本体の専用セーブスレッド（メインスレッドとは別）上で動き、メインスレッドとの排他制御（ロック）はメモリ上のデータを集める間だけに留めており、実際のディスク書き込みはロックを外した後に行うため、メインスレッド（＝ゲームプレイ）を待たせることはない
- **Restart・Give Up・ニューゲーム開始時**（`SaveLube.DeleteSaves`へのpostfix）: 進捗データのクリアとファイル削除

まとめると、フレームレートに影響しうる処理（毎フレーム走るもの）はいずれもゲーム本体が同じ場所で既に行っている処理より軽く、重い処理（ディスクI/O）は低頻度かつ別スレッドで行われるため、プレイ中の負荷は実質無視できるレベル。

## 必須環境

Harmonyを使用していますが、`0Harmony.dll`はこのmodに同梱されているため、別途サブスクライブする必要はありません。

## プロジェクト構成

| ファイル | 役割 |
|---|---|
| `ModEntry.cs` | modのエントリーポイント（`[JumpKingMod]`）。Harmonyパッチの適用、レベル開始時のフック、メイン・ポーズ両メニューへの設定項目登録を行う |
| `Settings.cs` | 総数表示パターン・挑戦回数カウンター・Personal BestのON/OFFを保持する設定データ。XMLでの読み込み/保存 |
| `AttemptCounterToggle.cs` / `PersonalBestToggle.cs` / `ProgressionDetailToggle.cs` | メイン・ポーズ両メニュー上のON/OFFチェックボックス |
| `TotalDisplayModeOption.cs` | 総数表示パターン（常に表示/表示しない/突破後に表示）を左右キーで選ぶオプション部品 |
| `LocationSettingsAccessor.cs` | `internal`な`LocationTextManager.SETTINGS`をリフレクションで読み、現在レベルのエリア一覧を取得する |
| `LocationResolver.cs` | 現在のスクリーン番号から、エリア範囲内（パターンA）かすき間（パターンB）かを判定する純粋ロジック |
| `LevelKeyResolver.cs` | 進捗データのキーとして使う、現在プレイ中のレベルの識別子を決める |
| `PlayerGroundChecker.cs` | プレイヤーが足場に立っているか（空中ではないか）を判定する |
| `PlayTimeAccessor.cs` | `internal`な`AchievementManager`をリフレクションで読み、現在のプレイ時間を取得する（Progression Detailのラップタイム用） |
| `AreaProgressStore.cs` | エリアごとの初回訪問順・挑戦回数・突破済みフラグ・最高到達画面・初到達時のプレイ時間を保持し、XMLで永続化する |
| `AreaTracker.cs` | レベル開始時のリセット、毎フレームの進捗更新、表示テキストの構築をまとめるコーディネーター |
| `AreaInfoTextInfo.cs` | ポーズ画面に表示するテキスト部品。描画直前に毎回テキストを再計算する（既存の`IStatInfo`と同じ方式） |
| `MenuFactoryPatches.cs` | `MenuFactory.CreatePauseInfo`・`PauseManager.SetPause`へのHarmonyパッチ。「Objective」欄の下に上記テキストを追加し、ポーズを開くたび枠サイズを再計算する |
| `LevelManagerPatches.cs` | `LevelManager.Update`へのHarmonyパッチ。ポーズ中以外の毎フレーム、進捗（挑戦回数・突破判定・最高到達地点）を更新する |
| `SaveLubePatches.cs` | `SaveLube`の保存・削除へのHarmonyパッチ。進捗データをゲーム本体のセーブライフサイクルと同期させる |
