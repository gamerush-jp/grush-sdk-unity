# GameRush SDK for Unity

GameRush の GameAPI を Unity から呼ぶための UPM パッケージ。ビルドの書き出し方は [対応エンジンと書き出しガイド](https://gamerush.jp/engines)。使い方の正（ランキング・公開プレイヤー状態・投稿が弾かれる条件とエラーコード・API トークン）は [SDK ガイド](https://gamerush.jp/sdk)。

## 導入

Package Manager の `Add package from git URL...` に次を入れる。

```text
https://github.com/gamerush-jp/grush-sdk-unity.git#v1.12.1
```

Unity 2021.3 以降。ビルドターゲットは WebGL。

## 使い方

```csharp
using GRushSdk;

var self = await GRush.Player.GetSelfAsync();
if (self.Ok)
{
    Debug.Log(self.Value.PseudoId);
}

var joined = await GRush.Net.JoinAsync("duel");
if (joined.Ok)
{
    var room = joined.Value;
    room.Message += message => Debug.Log(message.From);
    room.Error += error => Debug.LogWarning(error.Code + ": " + error.Message);
    room.Send(payload, GRushChannel.Unreliable, GRushRoom.Everyone);
}
```

部屋が1通を捨てると `GRushRoom.Error` が届く。`Code` は `"messageTooLarge"`・`"rateLimited"`・`"badFrame"` で、ほかの値が届くこともある。部屋はつながったままで、続けて送れる。部屋が閉じるときは `Closed` だけが届く。

`GRush` は例外を投げない。GameRush の外で動かした場合も `GRushResult<T>.Ok` が `false`、`Code` が `GRushErrorCode.Unsupported` になるだけで、ゲームは止まらない（アナリティクスは `bool` を返す。下記）。

### 相手探し

```csharp
await GRush.PlayerState.SetMineAsync("{\"deck\":\"fire\",\"level\":12}", -1, 1520);
var found = await GRush.PlayerState.SampleAsync(5, 1400, 1600);
if (found.Ok)
{
    foreach (var rival in found.Value.States) Debug.Log(rival.PseudoId + " " + rival.Rating);
}
```

`SampleAsync` は同じゲームの他プレイヤーの公開状態をランダムに返す（件数は 1〜20、既定 10）。自分と運営が伏せた状態は返らず、名前も返らない。範囲を渡すと、rating の無い状態は返らない。payload は `RawJson`（応答そのもの）から読む。`SetMineAsync` の第3引数の rating は -1e9 より大きく 1e9 より小さい数値で、`null` で消し、2引数の版では今の値のまま。`GRushPlayerState.HasRating` が false なら rating は書かれていない。`GRush.IsMatchmakingAvailable` が `false`（`protocolVersion` 7 未満）では `SampleAsync` が `Unsupported`、`SetMineAsync` は rating を送らずに payload だけを書く。

### 確認用の表示

GameRush の確認用の表示では、公開プレイヤー状態の写しがゲームに渡る。受け取ったら、その状態が他プレイヤーにどう見えるかを、普段と同じ描き方で描く。

```csharp
GRush.PlayerState.PreviewReceived += page =>
{
    var state = page.States[0];
    ShowRival(state.PseudoId, page.RawJson); // payload は RawJson の state.payload から読む
};
```

渡る `GRushPlayerStatePage` は `GetMineAsync` と同じ形で、`States` に1件、`Exists` が `true`、`RawJson` は `{"state":{...}}`。自分の状態とは限らない。`PreviewReceived` は次の `Update` で呼ばれ、同じ写しは1回だけ渡る。購読より前に届いた写しは購読したときに渡るので、起動の早いうちに購読しておけばよい。購読が1つ以上あるあいだだけ GameRush から受け取り、すべて外すと受け取りをやめる。普段のプレイでは呼ばれない。`GRush.IsPlayerStatePreviewAvailable` が `false`（`protocolVersion` 10 未満）と GameRush の外では呼ばれず、例外も投げない。`GRush.UseBackend` でバックエンドを差し替えても、登録済みの `PreviewReceived` は新しいバックエンドの写しを受け続ける。

### 受信箱

```csharp
await GRush.Inbox.SendAsync(rivalPseudoId, "battleResult", "{\"stage\":3,\"win\":true}");
var inbox = await GRush.Inbox.ListAsync(true);
if (inbox.Ok)
{
    foreach (var message in inbox.Value.Messages)
    {
        var result = JsonUtility.FromJson<BattleResult>(message.PayloadJson);
    }
}
```

同じゲームの疑似 ID 宛てに、小さい構造化メッセージ（非同期対戦の結果の通知など）を送る。ゲストも送受信できる。**payload で文章や画像は送れない。** payload は JSON オブジェクトで、文字列の値とキーは識別子（`^[A-Za-z0-9_.:-]{1,32}$`）だけ、ほかは数値・真偽値・null。入れ子は最上位を 1 段目として 4 段まで、値は合計 64 個まで、詰めた JSON で 1KB まで。`kind` は `^[A-Za-z0-9._:-]{1,32}$`。合わなければ `InvalidParams`。利用者どうしの文章や画像のやりとりには使わない。

`ListAsync(unreadOnly, limit, cursor)` は新しい順（`limit` は 1〜50、0 なら 20）。続きは `NextCursor` を渡して読む。`MarkReadAsync` / `RemoveAsync` は 1 回 50 件まで。同じゲームの疑似 ID でない宛先は `InvalidParams`、送りすぎ（毎時・毎日の上限）は `RateLimited` で、時間が経てば回復する。`ReportAsync(messageId)` は GameRush が確認を出し、承諾されたら `true`。通報すると送り手をブロックし、その相手のメッセージと出品は見えなくなる。ゲストは `SignInRequired`。`GRush.IsInboxAvailable` が `false`（`protocolVersion` 7 未満）ではどれも `Unsupported`。

### 交換所

```csharp
// 1. 出すアイテムをセーブで仮置きにし、2. operationId と一緒に保存してから出品する。
var created = await GRush.Trade.CreateAsync(
    "{\"item\":\"sword\",\"count\":1}",
    "{\"item\":\"gem\",\"count\":3}",
    save.EscrowOperationId,
    new GRushTradeOptions { OfferKey = "sword", WantKey = "gem" }
);

// 4. 終わった取引をセーブへ反映し、保存してから Ack する。5. 取引 ID で二度反映しない。
var mine = await GRush.Trade.MineAsync();
```

プレイヤーがアイテムを出品し、同じゲームの別のプレイヤーが `AcceptAsync(tradeId, giveJson)` で受ける。ゲストも出品・受諾できる。GameRush はアイテムの正当性を確かめず、交換が片方だけ成立しないことと、終わった取引を取りこぼさないことだけを保証する。

取りこぼさないために、次の順で使う。

1. 出品の前に、出すアイテムを手元のセーブで仮置き（使えない状態）にする
2. `operationId`（`^[A-Za-z0-9._:-]{1,64}$`）をセーブに保存してから `CreateAsync` する
3. 応答が失われたら、同じ `operationId` で呼び直す。同じ `operationId` の再送は、中身によらず先に作った出品を返す
4. `MineAsync` の `Pending` をセーブに反映し、セーブを永続化してから `AckAsync` する（`AckAsync` に渡せる id は 1 回 50 件まで）
5. 反映済みの取引 ID をセーブに持ち、同じ取引を二度反映しない

`Pending` の `Action` は `ReceiveGive`（出品者が `GiveJson` を受け取る）・`ReturnOffer`（出品者が `OfferJson` を戻す。取り下げ・期限切れ・運営の非表示）・`ReceiveOffer`（受けた側が `OfferJson` を受け取る）。`Unknown` は反映せず、Ack もしない。

受付中の出品が 10 件あるときの `CreateAsync` は `InvalidParams` で、待っても直らない（取り下げるか、成立・期限切れを待つ）。毎日 30 件の出品と毎時 30 件の受諾の上限は `RateLimited` で、時間が経てば回復する。

**offer・want・give で文章や画像は送れない。** 形は受信箱の payload と同じで、大きさは詰めた JSON で offer 2KB・want 1KB・give 2KB まで。`OfferJson` などはそのまま `JsonUtility.FromJson` でゲームの型に読める。利用者どうしの文章や画像のやりとりには使わない。

`AcceptAsync` は先に取られた・期限切れ・取り下げ・見えない出品をエラーにせず、`Outcome`（`Matched` / `Taken` / `Expired` / `Cancelled` / `NotFound`）で返す。`Trade` は `Matched` のときだけ入る（`HasTrade`）。同じ取引への再送は `Matched`。`GRushTradeOptions.To` に疑似 ID を入れると指名の出品になり、`ExpiresInSec` は 600〜604800（0 なら 3 日）。受付中の出品は 1 人 10 件まで。`ListAsync` は他人の公開の出品と自分宛ての指名の出品のうち、受付中のもの（新しい順）。`ReportAsync(tradeId)` は GameRush が確認を出し、承諾されたら `true`。通報すると出品者をブロックする。`GRush.IsTradeAvailable` が `false`（`protocolVersion` 7 未満）ではどれも `Unsupported`。

### 共有

```csharp
if (await GRush.Share.IsAvailableAsync())
{
    var shared = await GRush.Share.ShareScreenAsync("ステージ3をクリア");
}
```

GameRush の確認シートが出て、プレイヤーが送り先を押したときに共有が開く。返るのは `GRushShareStatus.Opened` / `Cancelled` だけ。`ShareScreenAsync` はゲームの canvas のスクショを送る（画面に DOM で重ねた文字は写らない）。自前の画像を送るなら `ShareAsync(text, texture.EncodeToPNG())`。共有は 5 秒に 1 回までなので、1 回のボタン操作で呼ぶのはどちらか一方にする。共有したことを条件に報酬を出さない。本文は 100 文字までで、URL と @メンションを含むと `InvalidParams`。古い GameRush（`protocolVersion` 3 未満）では `Unsupported`。

#### 起動パラメータを添える

```csharp
var launchParams = new Dictionary<string, string> { { "stage", "3" } };
var shared = await GRush.Share.ShareScreenAsync("ステージ3に挑戦", launchParams);
if (!shared.Ok && shared.Code == GRushErrorCode.Unsupported)
{
    shared = await GRush.Share.ShareScreenAsync("ステージ3に挑戦");
}
```

`ShareAsync(text, image, launchParams)` と `ShareScreenAsync(text, launchParams)` は、共有リンクに `?gp.stage=3` のように起動パラメータを載せる。リンクを開いた人のゲームでは `GRush.Launch` に渡る（下の「起動パラメータ」）。キーと値の制限は「起動パラメータ」と同じで、1つでも合わなければ共有を開かずに `InvalidParams`。`null` と空の辞書は渡さないときと同じ。古い GameRush（`GRush.ProtocolVersion` が `GRush.ShareLaunchProtocolVersion`（9）未満）では、共有を開かずに `Unsupported` を返すので、起動パラメータを外して呼び直せば共有できる。**リンクは誰にでも見え、そのまま転送される。** プレイヤーを識別できる値（招待コードなど）を入れない。受け取る側のゲームは値を信用せず、確かめてから使う。

### 表示言語

```csharp
GRush.Locale.Changed += locale => Debug.Log(locale.Locale);
var result = await GRush.Locale.GetAsync();
if (result.Ok)
{
    var tag = result.Value.Locale;
}
```

`GRushLocale` は `Locale`（BCP 47 のタグ。`ja` や `zh-Hans`）、`Source`（`user` / `system` / `device`。今後増えても動くように書く）、`Languages`（優先順）を持つ。`GRush.Locale.Current` は取得済みなら同期で読め、無ければ `null`。`Changed` は次の `Update` で配られるので、取得の途中の変更を取りこぼさないよう `GetAsync` より先に購読する。`GRush.IsLocaleAvailable` が `false`（`protocolVersion` 4 未満）では `GetAsync` が `Unsupported` を返す。`Source` は必ず入り、GameRush が渡さなければ `"device"`。`Languages` は null と空文字を除いた一覧で、空なら `[Locale]`。`GRush.UseBackend` でバックエンドを差し替えても、登録済みの `Changed` は新しいバックエンドの通知を受け続ける。

### 起動パラメータ

GameRush の URL に `gp.` を付けて載せたキーと値を読む。ゲームのページの URL が `/g/<コード>?gp.item=sword&gp.stage=3` なら `item` が `sword`、`stage` が `3`。シェアしたリンクから特定のアイテムやステージへ飛ばすのに使う。

```csharp
var result = await GRush.Launch.GetAsync();
if (result.Ok && result.Value.TryGet("item", out var item) && OwnsItem(item))
{
    OpenItem(item);
}
```

**値を信用しない。** リンクは誰でも作れる。アイテムが存在するか・プレイヤーが持っているか・ステージが解放済みかをゲームの側で確かめ、確かめられなければ無視する。

`GRushLaunch` は `Params`（`IReadOnlyDictionary<string, string>`）と `TryGet(key, out value)` を持つ。キーは英数字と `_-` で 32 文字まで（`__proto__` は使えない）、値は英数字と `._~:,-` で 128 文字まで（空も可）、8 個まで。形の合わない項目と、2回書かれたキーは GameRush が落とし、8 個を超えたら全部落とす。渡るのは、プレイヤーが最初に開いたスライドのゲームだけで、別のゲームへスワイプして戻ったときは `Params` が空。フィードではページを読み直すと空になるが、単体表示や埋め込みでは読み直すと同じ値がまた渡ることがあるので、何度渡っても困らないように書く。`GetAsync` は GameRush から届くまで待ち、`GRush.Launch.Current` は届く前なら `null`。`GRush.IsLaunchAvailable` が `false`（`protocolVersion` 8 未満）と GameRush の外では `GetAsync` が `Unsupported`、`Current` が `null`。GameRush のアプリ側が古くて答えられないときは、`Unsupported` ではなく空の `Params` が返る。

### アナリティクス

```csharp
using System.Collections.Generic;
using GRushSdk;

GRush.Analytics.LogOnboardingStep(1, "tutorial_start");
GRush.Analytics.LogFunnelStep("shop", "visit-1", 2, "item_selected");
GRush.Analytics.LogCustomEvent(
    "boss_defeated",
    1,
    new Dictionary<string, string> { { "stage", "3" } }
);
GRush.Analytics.LogEconomyEvent(
    GRushEconomyFlowType.Sink,
    "coin",
    50,
    120,
    GRushEconomyTransactionType.Shop,
    itemSku: "sword"
);
```

作者のアナリティクスへ、オンボーディングとファネルのステップ・カスタムイベント・通貨の入手と消費を送る。

- **ほかの API と違い、どれも同期で `bool` を返し、例外を投げない。** `await` せずにどこからでも呼べる。`true` は GameRush へ渡したことで、保存されたことではない
- 形が不正なとき（下の制限）は `false`。古い GameRush（`protocolVersion` 6 未満。`GRush.IsAnalyticsAvailable` が `false`）と GameRush の外では `false`。`GRush.Analytics.IsAvailable` は GameRush の中で送れるかを返す
- `step` は 1〜100。`value` は省略で 1、絶対値 1e9 まで。`amount` は正の値で 1e12 まで、`endingBalance`（その後の残高）は絶対値 1e15 まで
- `transactionType` の標準の値は `GRushEconomyTransactionType` の定数で、ほかの文字列も受ける
- イベント名・ファネル名・`funnelSessionId` は英数字と `._:-` で 64 文字まで、通貨と `transactionType` は 32 文字まで
- `stepName`・`itemSku`・`fields` の値は1行のテキストに直してから受ける。改行・制御文字・双方向の上書きと isolate は空白に直し、前後の空白を落としてから 40 文字（コードポイント）まで数える。直した結果が空（空白や見えない文字だけ）の `stepName` と `itemSku` は省略と同じに扱い、`fields` の値は拒む。`fields` は3つまで、キーは英数字と `_` で 32 文字まで
- オンボーディングとファネルの同じステップは、1セッションで最初の1回だけが残る。セッションはページを読み直しても続く。同じファネルを何度も通るなら、通るたびに別の `funnelSessionId` を渡す
- 名前・値・`fields` に、プレイヤーを識別できる情報を入れない
- GameRush の中でも、ゲームが前面に出るまでの分は溜めてから送る。溜められるのは 30 件までで、超えた分は `false`
- `true` を返しても、次の分は保存されない
  - 前面に出た後に GameRush 本体が掛ける上限を超えた分。本体は 500ms に1件、まとめて最大 30 件まで受け、送る前の列は合計 200 件まで
  - セッションあたりの保存の上限を超えた分。オンボーディングを含むファネルのステップ 200 件・カスタムイベント 500 件・エコノミー 500 件
  - 送れなかった分（送り直さない）

## エディタでの動作確認

WebGL 以外（エディタ・スタンドアロン）では自動的に `GRushMockBackend` が使われる。`GRushMock` で挙動を切り替える。

```csharp
GRushMock.SignedIn = true;
GRushMock.DisplayName = "Editor Player";
GRushMock.GrantProfileConsent = false;
GRushMock.UnreliableDropRate = 0.1;
GRushMock.ShareStatus = GRushShareStatus.Cancelled;

var opponent = GRushMock.AddPeer("Sparring Partner");
opponent.Received += message => opponent.Send(reply, GRushChannel.Unreliable, GRushRoom.Everyone);
```

`GRushMock.AddPeer` で作った相手は同じプロセス内の2人目の peer として部屋に入り、送受信が実際に往復する。部屋が8人で埋まっていれば `null` を返す。

モックの `GRush.Net.JoinAsync` は GameRush と同じく、`mode`（英数字と `._:-` の1〜32文字）か `roomCode`（英数字の4〜12文字）の形が違えば `InvalidParams` で失敗する。部屋が埋まっていれば `Unavailable` で失敗する。

`GRushMock.RaiseRoomError(code, message)` は、自分がいる部屋に `GRushRoom.Error` を届ける（次のフレーム以降に届き、部屋は閉じない）。部屋にいなければ `false` を返す。

表示言語のモックは `GRushMock.SetLocale("en")`（`Source` は `user`）で切り替わり、`Changed` が飛ぶ。値が変わらない `SetLocale` では飛ばない。`SetLocale(locale, source, languages)` の `languages` の null と空文字は捨て、残りが無ければ端末の言語から決める。未設定なら `Application.systemLanguage` から写した値を `Source = "device"` で返す。

起動パラメータのモックは `GRushMock.LaunchParams`（既定は空。例 `GRushMock.LaunchParams["item"] = "sword";`）をそのまま返す。GameRush の制限では確かめない。`GRushMock.Reset()` で空になる。

共有のモックは確認シートを出さず、`GRushMock.ShareStatus`（既定 `Opened`）を返す。`GRushMock.ShareAvailable = false` で共有できない環境を試せる。起動パラメータは GameRush と同じ制限で確かめ、合わなければ `InvalidParams`。本文と画像の検査はしない。

アナリティクスのモックは、GameRush と同じ検査をして、通ったイベントを1件ずつ Console に出し、直近の 200 件を `GRushMock.AnalyticsEvents`（GameRush へ渡すのと同じ形の JSON の配列）で返す（`GRushMock.Reset()` で空になる）。弾いたイベントは、GameRush と同じく理由ごとに1回だけ警告する（`GRushMock.Reset()` でまた出る）。どこへも送らない。前面に出るまでの 30 件の溜め・本体の上限・同じステップを最初の1回だけ残す扱い・セッションあたりの上限は再現しないので、モックでは `true` の分がすべて記録に残る。モックが名乗る `protocolVersion` は 10。

相手探し・受信箱・交換所のモックは、`GRushMock.Rivals` の 3 人の相手を持つ。相手はそれぞれ rating 付きの公開プレイヤー状態を持ち、届いたメッセージを同じ kind と payload で送り返す。はじめと `GRushMock.Reset()` の後は、最初の相手からのメッセージ1通と、相手の受付中の出品を2件持つ。payload・offer・want・give の形、`operationId` での冪等、受付中の出品の上限、受諾の `Outcome`、`Pending` と Ack は GameRush と同じ規則で動く。通報を承諾すると（`GRushMock.ConfirmInboxReport` / `ConfirmTradeReport`、既定 `true`）相手をブロックし、その相手のメッセージと出品は見えなくなる。プレイヤー状態の通報は伏せていない状態にだけ届き、確認の答えを `GRushMock.ConfirmPlayerStateReport`（既定 `true`）で決める。

```csharp
GRushMockInbox.Receive(GRushMock.Rivals[0], "battleResult", "{\"stage\":3,\"win\":false}");
var listingId = GRushMockTrades.OfferToSelf(GRushMock.Rivals[1], "{\"item\":\"bow\"}", "{\"item\":\"gem\"}");
GRushMockTrades.Accept(myTradeId, "{\"item\":\"gem\"}"); // 相手が受けて、自分の Pending に ReceiveGive が載る
GRushMockTrades.Expire(listingId);
GRushMockPlayerStates.Define("custom-rival", "{\"level\":3}", false, 1300);
GRushMockPlayerStates.Preview(GRushMock.Rivals[0], "{\"level\":3}"); // 確認用の表示の写しを渡す
```

確認用の表示のモックは `GRushMockPlayerStates.Preview(pseudoId, payloadJson)` で写しを1つ渡し、次の `Update` で `PreviewReceived` が呼ばれる。`Preview(pseudoId, payloadJson, revision, rating)` で revision と rating を決められる（既定は revision 1、rating は無し。`null` なら rating は無し）。updatedAt は呼んだ時刻（UTC）。購読が無いうちに渡した写しは、次に購読したときに1回だけ届く。疑似 ID が空・payload が JSON オブジェクトでない・revision が負・rating が NaN か無限大のときは何もしない。`GRushMockPlayerStates.Reset()` で、まだ届いていない写しを捨てる。

**`UnreliableDropRate` は既定 0 だが、出荷前に必ず 0 より大きくして試すこと。** WebSocket 中継では `unreliable` も落ちずに届くため、パケットが落ちる前提で書けているかを確認できる場所はエディタのモックだけになる。

### grush.json をモックで使う

ランキングの定義は、プロジェクトの直下（`Assets/` の隣）の `grush.json` に書ける。書式は [SDK ガイドのランキング](https://gamerush.jp/sdk#leaderboards)。

- エディタのモックは、最初のランキングの呼び出しの前にこのファイルを読み、その枠を宣言する。ファイルが無ければ何もしない。書式の誤りはアップロードと同じ文言で Console に警告し、その中身は使わない
- WebGL でビルドすると、このファイルがビルドの最上位（`index.html` の隣）へ写る。ファイルが無いのに出力先に `grush.json` が残っていれば、消さずに警告を出す（残ったままアップロードすると、その中身で定義を書き換える）。GameRush はアップロードされたビルドの最上位の `grush.json` だけを読むので、`Assets/` や `StreamingAssets/` に置いても定義にならない
- ファイルを自動で読むときは、`GRushMockLeaderboards.Define` で同じ key を宣言してあればそちらを使う。`GRushMockLeaderboards.LoadManifest(json)` に渡した定義は、`Define` の宣言を置き換える。`LoadManifest` を呼ぶと、ファイルは自動では読まない。`LoadManifest` は誤りを `InvalidParams` で返す
- `enabled: false` の枠は一覧に出ず、投稿と取得は宣言していない key と同じく `InvalidParams` になる。grush.json で `minValue` / `maxValue` を書かなかった枠は、GameRush と同じく投稿を受けない
- `period` が `daily` / `weekly` / `monthly` の枠は、GameRush と同じ期間キー（UTC）を返し、期間が変わると空からやり直す
- `GRushMockLeaderboards.Reset()` の後は、次のランキングの呼び出しでファイルを読み直す

## 推奨設定でビルド

メニューの `GameRush/GameRush ウィンドウ` を開くと、先頭に「推奨設定でビルド」が出る。ログインしなくても使える。WebGL の設定ごとに、いまの値・推奨値・合っているか（✓ / ✗）を一覧にする。

- **推奨設定を適用**: ✗ の項目だけを推奨値へ変える。変えた設定は元に戻さない（`GameRush/推奨設定を適用` からも同じ）。
- **推奨設定でビルド**: 設定を適用してから、Build Settings で有効なシーンを WebGL でビルドする（`GameRush/推奨設定でビルド` からも同じ）。出力先は既定で `<プロジェクト>/Build/GameRush`。**出力先のフォルダはビルドのたびに消して作り直す**ので、プロジェクトのフォルダそのものや `Assets/` `Packages/` `ProjectSettings/` `Library/` の中は選べない。既にあるフォルダは、中身が `Build/` `TemplateData/` `StreamingAssets/` `index.html` `grush.json` だけか空のときに限って消す。
- プロジェクトの直下に `grush.json` があれば、WebGL のビルドのたびに出力先の最上位へ写す（このウィンドウ以外からの WebGL ビルドでも写す）。
- ビルドが通ると、その出力先が「ビルドをアップロード」に入った状態になる。ログインしていればそのまま上げられる。
- 合計サイズの目安は 15 MB 以下。30 MB 以上は読み込みが遅く、スマホで落ちることもあるので危険。50 MB を超えるとアップロードできない。

主な推奨値は Brotli 圧縮・Decompression Fallback オフ・Threads Support オフ（GameRush の配信は COOP/COEP を付けない）・Data Caching オフ（GameRush が自前で先読みする）・WebGL 2 のみ・Development Build オフ。

CI などからはコマンドラインで同じことができる。

```text
Unity -batchmode -quit -projectPath <プロジェクト> -buildTarget WebGL -executeMethod GRushSdk.Editor.GRushBuild.BuildRecommended -grushOutput Build/GameRush
```

`-grushOutput` の相対パスはプロジェクトのフォルダから数える。ログの行は `[grush-build] ` で始まり、変えた設定は `set <項目>: <前> -> <後>` の形で出る。

| 終了コード | 意味 |
|---|---|
| 0 | ビルドでき、GameRush へ上げられる出力になった |
| 1 | ビルドに失敗した |
| 2 | 引数が無い・出力先が危ない・GameRush のビルド以外のファイルがある出力先・WebGL モジュールが無い・有効なシーンが無い |
| 3 | 想定外の例外 |
| 4 | ビルドはできたが、GameRush のアップロード規則に合わない出力になった |

## サンプル

Package Manager の Samples から取り込む。どちらもシーンを含まないので、空のシーンに GameObject を1つ作ってスクリプトを付ける。

| サンプル | 内容 |
|---|---|
| Score Attack | 疑似IDの取得と表示名の同意要求 |
| Duel | 2人対戦。エディタではモックの対戦相手が動く |
