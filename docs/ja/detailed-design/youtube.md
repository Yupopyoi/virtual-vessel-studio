# YouTube連携詳細設計

状態：案（2026-11-21）。利用者と、決めること（17章）とリスク（13章）を相談してから確定する。方式設計の変更（18章）も、確定と同時に行う。

## 1. 目的

YouTubeのアカウントと本アプリをつなぎ、配信の操作をアプリの中で済ませられるようにする（方式設計10.11の「後の段」）。

最初の目標は、配信のコメントを配信画面に出すことである（利用者の要望、2026-11-21。コメントを配信画面に重ねる仕組みは多くの配信者が外部のツールで使っている）。

段階：

1. 段階1（本書の対象）：Googleのアカウントでログインする（OAuth）。いま配信している自分の配信を見つけ、そのコメントを受け取り、配信画面の枠に表示する。
2. 段階2（後で）：配信枠の作成、タイトルと公開範囲、配信の開始と終了をYouTube APIで行う。書き込みの権限（`youtube`または`youtube.force-ssl`のスコープ）が要るので、段階1と分け、必要になったときに追加で許可を求める（Incremental Authorization）。
3. 段階3（後で）：ストリームキーを、APIから得たものに置き換える（利用者が貼り付けなくてよくなる）。

この設計に含めないもの：コメントへの返信と管理（削除、タイムアウト）、スーパーチャットの集計、ほかのサービス（Twitchなど）のコメント。ただし、コメントの型と表示は、サービスに依らない形にする（5章）。

## 2. 責務

| 責務 | 担当 |
|---|---|
| Googleのアカウントでのログイン（OAuth 2.0）、トークンの更新と取り消し | Streaming（`YouTubeAccount`） |
| トークンの保存 | ProjectData（`ICredentialStore`、Windowsの資格情報マネージャー。方式設計6.13） |
| 自分のいまの配信を見つける、コメントを受け取る | Streaming（`YouTubeLiveChat`） |
| コメントを、サービスに依らない形（`LiveChatMessage`）にする | Streaming |
| コメントの絞り込み（NGワード、表示する数、長さ） | Streaming（`ChatFilter`、純粋関数） |
| コメントを画像に描く | UI（`CommentOverlay`。UI Toolkitで描画用のTextureへ） |
| 配信画面のどこに出すか（枠） | Stage（`CommentFrame`、`IFrameSource`。アバターの枠と同じ扱い） |
| ログイン、表示の設定（設定画面）、コメントの表示のオンオフ（ライブ画面） | UI |

## 3. Moduleの境界

- YouTube APIは、Streaming Moduleの中の`Streaming/YouTube/`に閉じ込める。方式設計10.11がYouTube連携を配信の方式に置いているためである。新しいModuleは作らない。
- ほかのModuleが見るのは、`IYouTubeAccount`（ログインの状態と操作）と`ILiveChat`（コメントの流れ）、値の型`LiveChatMessage`だけとする。YouTube APIのJSON、トークン、HTTPの型は外に出さない。
- `ILiveChat`はサービスに依らない。後でTwitchなどを加えるときは、同じ`ILiveChat`の実装を加える（CLAUDE.md 5章）。
- コメントを描くのはUI、置く場所を決めるのはStage、配信の映像に重ねるのはVideoである（CLAUDE.md 6章のCaptureと同じ分け方）。StageはYouTubeを知らない。
- 外部のプロセスやPythonは使わない。HTTPSは.NETの`HttpClient`で行う（Voice Labのダウンロードと同じ）。

## 4. 主なClassとInterface

| Class | 種類 | 役割 |
|---|---|---|
| `IYouTubeAccount` | Interface（Streaming） | ログインの状態（未ログイン、ログイン中、ログイン済み、失敗）、チャンネル名、ログイン、ログアウト |
| `YouTubeAccount` | Class（Streaming） | OAuth 2.0（ループバックのリダイレクトとPKCE）、トークンの保存、更新、取り消し |
| `LoopbackRedirect` | Class（Streaming） | `127.0.0.1`の空いているポートで、Googleからの戻りを1回だけ受ける |
| `OAuthMath` | static（Streaming） | PKCEの値、認可のURL、戻りのURLの検査（`state`の照合）。純粋関数 |
| `YouTubeApi` | Class（Streaming） | REST APIの呼び出し：自分の配信（`liveBroadcasts.list`）、コメントのストリーム（`liveChatMessages.streamList`） |
| `ILiveChat` | Interface（Streaming） | コメントの流れ：状態、最近のコメント、受け取りの開始と停止 |
| `YouTubeLiveChat` | Class（Streaming） | 配信が始まると自分の配信を探し、コメントを受け取り、切れたらつなぎ直す |
| `LiveChatMessage` | Record（Streaming） | サービスに依らないコメント：ID、時刻、名前、本文、種類（通常、スーパーチャットなど）、配信者自身か、モデレーターか、メンバーか |
| `ChatFilter` | static（Streaming） | NGワード、制御文字の除去、長さの上限。純粋関数 |
| `CommentOverlay` | Class（UI） | 最近のコメントを、UI Toolkitのパネルで描画用のTextureへ描く |
| `CommentFrame` | Class（Stage） | 配置編集のコメントの枠（`IFrameSource`）。Textureを平面の映像の層に置く |

## 5. 公開API

```csharp
public interface IYouTubeAccount
{
    IReadOnlyObservable<YouTubeAccountState> State { get; } // SignedOut、SigningIn、SignedIn、Failed
    string ChannelTitle { get; }                            // ログイン済みのときのチャンネル名。それ以外はnull
    Task<YouTubeAccountProblem> SignInAsync(CancellationToken cancellationToken); // 既定のブラウザーを開いて待つ
    Task SignOutAsync();                                    // Googleでトークンを取り消し、保存を消す
}

public interface ILiveChat
{
    IReadOnlyObservable<LiveChatState> State { get; }       // Off、Waiting（配信を探している）、Receiving、Unavailable
    IReadOnlyObservable<LiveChatProblem> Problem { get; }    // 12章
    IReadOnlyList<LiveChatMessage> Recent { get; }          // 絞り込んだ後の最近のもの（上限50）。メインスレッドで読む
    event Action Received;                                  // 新しいコメントの後。メインスレッドで
    void Start();                                           // 配信の開始とともに、Compositionが呼ぶ
    void Stop();
}
```

- `ILiveChat.Start`は、配信の状態（`IStreamingService.State`がLive）に合わせてCompositionが呼ぶ。利用者がコメントの表示を切っているときは呼ばない。
- 受け取ったコメントは、受け取りのスレッドからメインスレッドへ渡す（`ConcurrentQueue`とFrame Loop）。

## 6. Data Model

### 6.1 設定

`Profiles/Streaming/<id>.json`の`stream`に加える（Schema version 1のまま。ない項目は既定）：

```json
"youtube": {
  "credentialId": "<16進数32桁>",
  "showComments": true,
  "comments": { "max": 8, "ngWords": ["..."], "showNames": true }
}
```

- `credentialId`は、トークンを保存する資格情報のID（配信のキーと同じ形。配信詳細設計11章）。トークンそのものはProfileに書かない。
- 枠の位置と大きさは`stage-settings.json`の`screens.comments`（Stage詳細設計6.3と同じ形）。

### 6.2 トークン

- 保存するのはリフレッシュトークンだけである。アクセストークン（約1時間）は、メモリーにだけ持つ。
- 資格情報マネージャーの対象名は`VirtualVesselStudio/youtube/<credentialId>`、そのユーザーだけ、そのPCだけ（`CRED_PERSIST_LOCAL_MACHINE`）。大きさの上限（約2.5 KB）に収まる。

### 6.3 アプリの登録（クライアントID）

- Google Cloudのプロジェクトに、種類「デスクトップアプリ」のOAuthクライアントを作る。デスクトップのアプリに埋め込むクライアントシークレットは、秘密として扱えないとGoogleが明示している。そのためPKCEで守る（7.1）。
- クライアントIDとシークレットは、リポジトリに書かない。リリースのビルドで、GitHubのSecretから埋め込む。開発者は自分のプロジェクトの値を、リポジトリの外の開発用の設定ファイルに書く。フォークがこのプロジェクトのクオータを使わないためである。
- 値がないビルドでは、YouTube連携の画面に「このビルドでは使えません」と出し、ほかの機能は動く。

## 7. 状態とライフサイクル

### 7.1 ログイン（OAuth 2.0、インストールされたアプリ）

1. `LoopbackRedirect`が`127.0.0.1`の空いているポートで待つ（`TcpListener`。管理者の権限もURLの予約も要らない）。
2. PKCEの`code_verifier`（ランダムな43〜128文字）と、その`S256`の`code_challenge`、ランダムな`state`を作る。
3. 既定のブラウザーで、Googleの認可のURL（スコープは`youtube.readonly`だけ）を開く。アプリの中のWebViewは使わない（Googleが禁じている。利用者がアドレスを確かめられないため）。
4. 利用者が許可すると、ブラウザーが`http://127.0.0.1:<port>/?code=...&state=...`に戻る。`state`が違う、`error`がある、5分たった、のどれかなら失敗とする。最初の1回の要求だけを受け、すぐに待つのをやめる。ブラウザーには「アプリに戻ってください」という短いページを返す。
5. 認可コードと`code_verifier`で、トークンを取る（`https://oauth2.googleapis.com/token`）。
6. リフレッシュトークンを資格情報マネージャーへ保存し、`channels.list mine=true`でチャンネル名を得る。

- ログアウトは、Googleの取り消しのURL（`https://oauth2.googleapis.com/revoke`）でトークンを無効にしてから、保存を消す。取り消しに失敗しても（オフラインなど）保存は消し、利用者には、Googleのアカウントの「サードパーティのアクセス」からも外せると伝える。
- リフレッシュトークンが無効になったら（利用者がGoogleの側で外した、など）、保存を消して未ログインに戻し、もう一度ログインするよう伝える。

### 7.2 コメントの受け取り

1. 配信がLiveになると、`liveBroadcasts.list`（`mine=true`、`broadcastStatus=active`）で自分のいまの配信と`liveChatId`を探す（1ユニット）。見つからなければ、30秒おきに最大10回探す（配信の開始からYouTubeで公開されるまで時間がかかるため）。
2. `liveChatMessages.streamList`でコメントのストリームを開く（5ユニット）。サーバーが新しいコメントを、開いたままの接続で送ってくる。
3. 切れたら、最後の`nextPageToken`から、間を空けてつなぎ直す（1秒、2秒、4秒…最大60秒）。
4. 配信が終わったら閉じる。

- 繰り返し問い合わせる`liveChatMessages.list`は使わない。1回に5ユニットかかり、5秒ごとに2時間問い合わせると約7,200ユニットになる。1日のクオータ（10,000、全利用者で共有。13章）がすぐに尽きる。
- `streamList`はgRPCとHTTPの両方で使える。UnityのMonoの`HttpClient`はHTTP/2を使えないため、HTTPの方を使う。HTTPでのメッセージの区切り方は文書にないので、実装の前に試作で確かめる（17章）。

## 8. 主な流れ

### 8.1 コメントを出す

1. `YouTubeLiveChat`がコメントを受け取り、`LiveChatMessage`にする。
2. `ChatFilter`が、制御文字と書字方向を変える文字を除き、長さを200文字までにし、NGワードを含むものを捨てる。
3. メインスレッドで`Recent`に加え、`Received`を出す。
4. `CommentOverlay`が、最近の`max`件を描き直す（新しいものが下）。名前と本文はただの文字として描き、Rich Textとして解釈しない。
5. `CommentFrame`がそのTextureを、配置編集で決めた枠に置く。

- 描き直すのは、コメントが来たときだけである。毎Frameは描かない。

## 9. Threadと非同期

- 受け取りのスレッド（`Task`）：HTTPのストリームを読み、JSONを解き、`ConcurrentQueue`へ入れる。メインスレッドを待たない。
- メインスレッド：Frame Loopで、キューからコメントを取り出す（1Frameに最大20件）。
- ログインの待ち受けは、`async`で、取り消しと5分の期限を持つ。

## 10. 設定

6.1のとおり。設定画面の「配信」のページに「YouTube」のCardを置く（UIは別に画面仕様を作る）：

- ログイン：「Googleでログイン」、ログイン済みならチャンネル名と「ログアウト」。
- コメントの表示：オンオフ、表示する数、名前を出すか、NGワード。
- 枠は配置編集で動かす（ゲーム画面やアバターと同じ）。

## 11. 保存

- リフレッシュトークン：資格情報マネージャー（6.2）。
- コメント：保存しない。メモリーに最近の50件を持つだけで、配信が終わったら捨てる。
- チャンネル名：表示のためにメモリーに持つだけ。

## 12. エラーと回復

| 起きたこと | 動作 | 利用者への表示 |
|---|---|---|
| このビルドにクライアントIDがない | YouTube連携を使えない | このビルドでは使えません |
| ログインを取り消した、5分たった | 未ログインのまま | ログインできませんでした。もう一度試してください |
| リフレッシュトークンが無効 | 保存を消し、未ログインへ | もう一度ログインしてください |
| いまの配信が見つからない | 30秒おきに探す。10回でやめる | 配信が見つかりません。YouTube Studioで配信が始まっているか確かめてください |
| コメントが無効の配信 | 受け取らない | この配信はコメントがオフです |
| クオータが尽きた（403 `quotaExceeded`） | その日は受け取らない | 今日はコメントを受け取れません |
| 接続が切れた | つなぎ直す（7.2） | 表示しない。続けば「コメントを受け取れません」 |

- どれも配信と録画を止めない（方式設計12.11と同じ考え）。

## 13. セキュリティとプライバシー

相談する点（利用者の確認を待つ）。対策の欄は本書の案である。

| # | 脅威、リスク | 対策 | 残るもの |
|---|---|---|---|
| S1 | 同じユーザーで動くマルウェアが、資格情報マネージャーからリフレッシュトークンを読む | スコープを読み取りだけ（`youtube.readonly`）にし、盗まれても配信の操作や投稿はできない。ログアウトで取り消す | 読まれること自体は防げない（ストリームキーと同じ。配信詳細設計11章）。チャンネルの非公開の情報（非公開の配信の一覧など）は読まれうる |
| S2 | ほかのローカルのプログラムが、ループバックのリダイレクトから認可コードを横取りする | PKCE（S256）、ランダムな`state`の照合、`127.0.0.1`だけ、空いているポートをその都度、1回で閉じる、5分の期限 | PKCEにより、コードだけでは使えない |
| S3 | アプリから抜き出したクライアントIDとシークレットで、ほかの人が本アプリのふりをする。または、本アプリのクオータを使い切る | 値をリポジトリに置かずリリースだけに埋め込む。同意の画面でアプリ名とアクセスの内容を示す。クオータの使い方を診断に出す | デスクトップのアプリでは完全には防げない（Googleも秘密として扱わない）。クオータを使い切られると、全利用者のコメントが止まる |
| S4 | コメントの中身による攻撃：Rich Textのタグ（大きな文字、画像）、とても長い文、書字方向を変える文字、大量の投稿 | ただの文字として描く、制御文字と書字方向の文字を除く、200文字まで、表示はmax件まで、コメントの中のリンクは開かない | 見た目の荒らし（絵文字の連投など）は残る。NGワードで減らす |
| S5 | 配信に出してはいけないコメント（個人情報、嫌がらせ）が画面に出る | NGワード、表示のオンオフ（ライブ画面ですぐ切れる）、YouTubeの側の保留（自動のモデレーション）に従う | 出てから消すまでの間は配信に映る。表示を遅らせる機能は段階1では作らない（相談） |
| S6 | 視聴者の名前とコメントが、ディスクやログに残る | 保存しない。ログは件数と状態だけ。診断にも中身を出さない | なし |
| S7 | 外部の画像（アイコン）を読み込むことによる追跡や、画像の解析の不具合 | 段階1ではアイコンを出さない | なし |
| S8 | 通信の盗聴、なりすまし | HTTPSだけ。証明書はWindowsの通常の検証で、飛ばす設定は作らない | なし |
| S9 | YouTube APIの規約（YouTube API Services Terms、Developer Policies）に違反する | プライバシーポリシーを公開する、取得したデータを保存しない、ブランドの表記に従う、非公式の方法（ページの解析、内部API）は使わない | 規約の確認と、Googleの審査（17章） |
| S10 | 審査を通るまで、Googleの同意の画面に「確認されていないアプリ」と出る。利用者は100人まで。公開の状態を「テスト」にしている間は、登録したテストユーザーしか使えず、リフレッシュトークンは7日で切れる | 審査を申請する（プライバシーポリシー、ホームページ、ドメインの確認、動画） | 審査に通るまでは広く配れない |

- 非公式の方法（YouTubeのページの解析や内部のAPI）でコメントを取る方法は、ログインもクオータも要らないが、YouTubeの利用規約に反し、予告なく壊れるので採らない。

## 14. Testの方針

| 種類 | 対象 |
|---|---|
| EditMode | `OAuthMath`：PKCEの値（RFC 7636の例と一致）、`state`の照合、戻りのURLの検査。`ChatFilter`：制御文字、書字方向、長さ、NGワード。`LiveChatMessage`への変換（APIのJSONの例から）。つなぎ直しの間隔。トークンの保存と削除（偽の資格情報の保存先）。`LoopbackRedirect`：正しい戻り、`state`の違い、2回目の要求、期限 |
| PlayMode | 設定画面のログイン、ログアウト、表示の設定。コメントの枠が配置編集に出ること。`CommentOverlay`がコメントをただの文字として描くこと（タグを含む名前） |
| 試作（実装の前） | HTTPの`streamList`のメッセージの区切り方。自分のテストの配信で、コメントが届くまでの時間と、つなぎ直し |
| 実機（手動） | 実際のアカウントでのログインとログアウト、Googleのアカウントの画面からの取り消し、配信中のコメントの表示 |

- 自動テストは本物のGoogleにつながない。HTTPは偽のハンドラーで置き換える。

## 15. 性能

- 受け取りはメインスレッドの外で行う。メインスレッドの仕事は、コメントが来たときの描き直しだけである。
- 描画用のTextureは枠の大きさに合わせ、配信の解像度を超えない。
- クオータ：1回の配信で、配信を探すのに約1〜10ユニット、ストリームに5ユニット（つなぎ直しのたびに5）。10,000ユニットで、1日に約1,000回の配信まで（全利用者の合計）。

## 16. Folder / Namespace

```text
Streaming/Runtime/YouTube/   IYouTubeAccount、YouTubeAccount、LoopbackRedirect、OAuthMath、YouTubeApi、YouTubeLiveChat
Streaming/Runtime/Chat/      ILiveChat、LiveChatMessage、ChatFilter
UI/Runtime/Comments/         CommentOverlay、YouTubeのCard
Stage/Runtime/               CommentFrame
```

## 17. 決めること、確かめること

利用者に決めてもらうこと：

1. Google Cloudのプロジェクトとクライアントを誰の名義で作るか。審査の申請（プライバシーポリシー、ホームページ、ドメイン）を行うか。それまでは100人までで、同意の画面に「確認されていないアプリ」と出る。
2. クオータ（1日10,000ユニット、全利用者で共有）で始め、利用者が増えたら増加を申請する方針でよいか。
3. 段階1のスコープを`youtube.readonly`だけにすること。
4. 不適切なコメントへの対策を、段階1ではNGワードと表示のオンオフにし、表示の遅延やユーザーごとの非表示は後にすること。
5. 本書の範囲：コメントの表示から始め、配信枠の作成などは段階2とすること。

実装の前に確かめること：

- HTTPの`streamList`のメッセージの区切り方（試作）。使えなければ、ネイティブのWinHTTP（HTTP/2）でgRPCを話す方法を検討する。
- `streamList`が`youtube.readonly`のアクセストークンで使えること。
- YouTube API Services Termsの、表示と保存についての条件。

## 18. 方式設計の変更（確定と同時に行う）

- 10.11：段階1（ログインとコメントの表示）と段階2、3を書く。
- 10.12、6.13：OAuthのリフレッシュトークンを資格情報マネージャーに置くこと、スコープを最小にすること。
- 11章（Stage）：配信画面に重ねる要素として、コメントの枠を加える。
- 16章：Twitchなどほかのサービスのコメントを、同じ`ILiveChat`で加えられること。
