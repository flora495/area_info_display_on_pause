# AreaInfoDisplayOnPause

Jump Kingのmodです。ポーズ画面の「Objective」欄の下に、現在いるエリアの名前・そのエリアの何枚目にいるか・そのエリアへの挑戦回数を表示します。

表示例: `Icy Caverns 3/5 (2回目)`

- エリアの境界はゲーム本体の`gui/location_settings.xml`（バニラ・カスタムレベル共通の仕組み）をそのまま使うため、追加データなしでバニラ・対応済みカスタムレベルの両方で動作します
- エリア定義の範囲外（次のエリアへ向かう途中の「すき間」）にいる場合は、直前のエリア名を引き継いで枚数だけ伸び続ける表示になります
- エリアの総数（`n/m`の`m`）を表示するかどうかは3パターンから選べます（「常に表示」「表示しない」「そのエリアを突破した後だけ表示」）
- 挑戦回数はそのプレイ（セーブ）中に各エリアへ初めて入った順番を実測し、下に落ちてから再進入するたびに加算されます

詳しい調査経緯・仕様検討の過程は[NOTES.md](NOTES.md)を参照してください。

## 使い方

メインメニュー（ホーム画面）→ **Mods** → **AreaInfoDisplayOnPause** を開き、

1. **Enabled** にチェックを入れる
2. **Show Total** で総数表示のパターンを選ぶ
3. **Attempt Counter** で挑戦回数表示のON/OFFを切り替える

設定はメインメニューからのみ行えます（ゲーム中のポーズメニューには設定項目は表示されません）。

設定・進捗データはmod自身のdllと同じフォルダに、それぞれ`F.AreaInfoDisplayOnPause.Settings.xml`・`F.AreaInfoDisplayOnPause.AreaProgress.xml`として保存されます。進捗データはゲーム本体のセーブ・ロード・削除と同期します。

## 必須環境

Harmonyを使用していますが、`0Harmony.dll`はこのmodに同梱されているため、別途サブスクライブする必要はありません。

## プロジェクト構成

| ファイル | 役割 |
|---|---|
| `ModEntry.cs` | modのエントリーポイント（`[JumpKingMod]`）。Harmonyパッチの適用、レベル開始時のフック、メインメニューへの設定項目登録を行う |
| `Settings.cs` | 有効/無効・総数表示パターン・挑戦回数カウンターのON/OFFを保持する設定データ。XMLでの読み込み/保存 |
| `EnabledToggle.cs` / `AttemptCounterToggle.cs` | メインメニュー上のON/OFFチェックボックス |
| `TotalDisplayModeOption.cs` | 総数表示パターン（常に表示/表示しない/突破後に表示）を左右キーで選ぶオプション部品 |
| `LocationSettingsAccessor.cs` | `internal`な`LocationTextManager.SETTINGS`をリフレクションで読み、現在レベルのエリア一覧を取得する |
| `LocationResolver.cs` | 現在のスクリーン番号から、エリア範囲内（パターンA）かすき間（パターンB）かを判定する純粋ロジック |
| `LevelKeyResolver.cs` | 進捗データのキーとして使う、現在プレイ中のレベルの識別子を決める |
| `AreaProgressStore.cs` | エリアごとの初回訪問順・挑戦回数・突破済みフラグを保持し、XMLで永続化する |
| `AreaTracker.cs` | レベル開始時のリセット、毎フレームの進捗更新、表示テキストの構築をまとめるコーディネーター |
| `AreaInfoTextInfo.cs` | ポーズ画面に表示するテキスト部品。描画直前に毎回テキストを再計算する（既存の`IStatInfo`と同じ方式） |
| `MenuFactoryPatches.cs` | `MenuFactory.CreatePauseInfo`へのHarmonyパッチ。「Objective」欄の下に上記テキストを追加する |
| `LevelManagerPatches.cs` | `LevelManager.Update`へのHarmonyパッチ。ポーズ中以外の毎フレーム、進捗（挑戦回数・突破判定）を更新する |
| `SaveLubePatches.cs` | `SaveLube`の保存・削除・起動時ロードへのHarmonyパッチ。進捗データをゲーム本体のセーブライフサイクルと同期させる |
