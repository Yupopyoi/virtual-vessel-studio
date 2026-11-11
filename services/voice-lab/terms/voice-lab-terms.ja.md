<!-- version: 0.1-draft -->
<!-- 案（2026-11-11）。公開前に法律の専門家の確認を受ける（Voice Lab詳細設計8.2、17）。 -->

# Voice Lab 利用規約と免責事項

Voice Lab は、声を設計し、声の変換に使う声のモデルを作成する機能です。Voice Lab を使用するには、この内容に同意する必要があります。同意しない場合も、Voice Lab 以外の機能はそのまま使用できます。

## 1. 禁止事項

Voice Lab で作成した声、および声の変換を、次の目的に使用しないでください。

- 他人へのなりすまし
- 詐欺、その他の違法行為
- 偽情報の作成または拡散
- 本人の許可のない、他人の声の再現
- 他人の権利（肖像権、パブリシティ権、著作権など）を侵害する行為

プロンプトや追加の言葉で、実在の人物の声をまねる指示は行わないでください。

## 2. 学習に使う声

声のモデルの学習には、次のいずれかの声だけを使用してください。

- 自分自身の声
- 使用する権利を持つ声（本人から許可を得た声を含む）
- Voice Lab が生成した声

録音を取り込む機能を使う場合は、取り込む前に、この条件を満たすことを確認してください。

## 3. AI で生成した声であることの明示

作成した声や、声の変換で作った音声を公開する場合は、AI で生成または変換した声であることを、視聴者に分かる形で明示してください。

## 4. 責任

作成した声と、その使用によって生じた結果については、使用者が責任を負います。配信や公開の前に、使用する場所の規約やガイドラインを確認してください。

## 5. 無保証と責任の制限

Voice Lab と、Voice Lab が取得する外部の部品は、現状のまま提供されます。特定の目的への適合性、正確性、継続した提供を含め、いかなる保証もしません。外部の部品の多くは MIT ライセンスまたは Apache License 2.0 で提供されており、それぞれのライセンスも無保証です。

Virtual Vessel Studio の開発者は、法令で認められる範囲で、Voice Lab の使用または使用できないことによって生じた損害について責任を負いません。

## 6. 取得する外部の部品

Voice Lab を使用できるようにするとき、次の部品をインターネットから取得し、この PC に構築します。Python のパッケージを含め、構築後の合計は約 13 GB です。構築の途中は、一時的に最大で約 30 GB の空き容量が必要です。

| 部品 | 取得元 | ライセンス |
|---|---|---|
| uv（構築に使う道具） | GitHub（astral-sh/uv） | MIT または Apache-2.0 |
| Python 3.12 | uv の配布物（python-build-standalone） | Python Software Foundation License |
| Python のパッケージ（PyTorch など） | PyPI、PyTorch の配布元 | 各パッケージのライセンス |
| RVC（Retrieval-based Voice Conversion WebUI） | GitHub | MIT |
| RVC の事前学習モデル、RMVPE、HuBERT | Hugging Face（lj1995/VoiceConversionWebUI） | MIT |
| VoxCPM2 | Hugging Face（openbmb/VoxCPM2） | Apache-2.0 |
| 声のひな形、配信用の共有モデル | Virtual Vessel Studio の GitHub Release | MIT |

取得元から取得できない場合は、Virtual Vessel Studio の GitHub Release に置いた控えから、同じファイルであることを確かめて取得します。すべてのファイルは、取得後にハッシュ値で内容を確認し、一致しないものは使用しません。

RVC の事前学習モデルは、次のデータで学習されています。

> CSTR VCTK Corpus: English Multi-speaker Corpus for CSTR Voice Cloning Toolkit (version 0.92), Junichi Yamagishi, Christophe Veaux, Kirsten MacDonald, The Centre for Speech Technology Research (CSTR), University of Edinburgh. Creative Commons Attribution 4.0 International (CC BY 4.0).

VoxCPM2 の提供元は、なりすまし、詐欺、偽情報への使用を禁じ、AI で生成した音声であることの明示を求めています。第1条と第3条は、この条件を含みます。

各部品のライセンスの全文は、設定の「クレジットとライセンス」から確認できます。

## 7. 通信とデータの扱い

- 部品の取得には、インターネットへの接続が必要です。取得するのは第6条の取得元だけです。
- 声の設計、学習用の音声の作成、学習、声の変換は、すべてこの PC の中で行います。録音、学習用の音声、作成した声を外部に送信しません。
- 構築した環境は、Voice Lab の「作った声」から削除できます。削除しても、登録した声での配信には影響しません。

## 8. 内容の変更

この内容には版があります。内容を変更した場合は、Voice Lab で声を作成する前に、改めて同意を求めます。登録した声での配信は止まりません。
