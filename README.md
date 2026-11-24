# Outfit Bone Sync（衣装のボーン合わせ）

アバターのボーン（Armature）を改変したときに、改変前の体用の衣装を、改変した分だけ動かして合わせる Unity エディターのツールです。
使い方はパッケージの [README](Packages/jp.mosin.outfit-bone-sync/README.md) を見てください。

## 導入

VCC（VRChat Creator Companion）か ALCOM に、リポジトリ `https://vpm.mosin.jp/index.json` を追加し、プロジェクトに **Outfit Bone Sync（衣装のボーン合わせ）** を追加します。

## リポジトリの構成

- `Packages/jp.mosin.outfit-bone-sync/`: パッケージ本体。VPM（VRChat Package Manager）で配るのはこのフォルダの中身だけです。
- それ以外: VRChat 公式の雛形 [vrchat-community/template-package](https://github.com/vrchat-community/template-package) の Unity プロジェクトと Workflow です。
- `.github/workflows/dispatch-listing.yml`: 雛形にはない Workflow です。Build Release が成功したら、一覧リポジトリ [ryoctrl/vpm-listing](https://github.com/ryoctrl/vpm-listing) の Build Repo Listing を起動します。

## 版の公開

1. `Packages/jp.mosin.outfit-bone-sync/package.json` の `version` を上げて、main に push します。
2. Actions → **Build Release** → **Run workflow** を実行します（`gh workflow run release.yml -R ryoctrl/OutfitBoneSync` でも同じ）。`version` と同じ名前のタグと Release ができ、ZIP・unitypackage・package.json が添付されます。
3. Build Release が成功すると **Dispatch Listing Build** が自動で動き、一覧（`https://vpm.mosin.jp/index.json`）に新しい版が載ります。

## 設定（リポジトリ作成時に 1 回）

- Variable `PACKAGE_NAME`: `jp.mosin.outfit-bone-sync`（Build Release が使う）
- Secret `LISTING_DISPATCH_TOKEN`: 一覧リポジトリの Actions だけを操作できるトークン（作り方は [ryoctrl/vpm-listing の README](https://github.com/ryoctrl/vpm-listing#一覧を作り直すトークン)）

## ライセンス

MIT（[LICENSE](LICENSE)）
