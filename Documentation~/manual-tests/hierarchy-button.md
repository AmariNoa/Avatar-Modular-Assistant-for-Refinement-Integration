# 手動テストケース: Hierarchy ボタン（共有レジストリ対応）

- 対象: `Editor/Hierarchy/AmariHierarchyButton.cs`、`Editor/Hierarchy/AmariHierarchyButtonRegistry.cs`
- 作成日: 2026-08-16

## 共通の前提条件

- 本パッケージを導入した Unity プロジェクトを開き、シーンに `VRCAvatarDescriptor` を持つアバターを 1 体以上配置しておく。
- ディスプレイスケール 100% の環境を基準とする（125% / 150% は補足確認）。

## TC1: 単独導入時の表示

- 前提: FaceEmo（jp.suzuryg.face-emo）と Materilune（com.amari-noa.materilune）を導入していない。
- 操作: Hierarchy でアバタールートの行を表示する。
- 期待: 行の右端に黒背景・白文字の「AMARI」ボタンが表示される。行の上下中央に収まり、クリックで Avatar Customize ウィンドウが開く。

## TC2: FaceEmo 併用時の横位置

- 前提: FaceEmo を導入している。FaceEmo の Hierarchy アイコンは表示設定（既定）のまま。
- 操作: アバタールートの行を表示する。
- 期待: AMARI ボタンが FaceEmo ボタンの左側に、重ならずに表示される（FaceEmo の幅 30px + オフセット設定値〔既定 20〕+ 間隔 2px を空ける）。

## TC3: FaceEmo のオフセット設定変更への追従

- 前提: TC2 と同じ。
- 操作: EditorPrefs の `FaceEmo_HierarchyIconOffset` を既定 20 から変更し（例: 40）、Hierarchy を再描画する。
- 期待: AMARI ボタンが変更後の値に応じて左へ移動し、FaceEmo ボタンと重ならない（旧実装は固定 50px のため、この操作で重なりが発生していた）。

## TC4: FaceEmo アイコン非表示時の詰め

- 前提: FaceEmo を導入している。
- 操作: EditorPrefs の `FaceEmo_HideHierarchyIcon` を true にして Hierarchy を再描画する。
- 期待: FaceEmo ボタンが消え、AMARI ボタンが行の右端まで詰まる（非表示の FaceEmo のために空白を予約しない）。

## TC5: Materilune 併用時の並び

- 前提: Materilune を導入し、対象アバターを Materilune セットアップ済みにする（アバタールート行に Mt ボタンが出る状態）。
- 操作: ドメインリロード（スクリプト再コンパイルなど）を挟んでからアバタールートの行を表示する。
- 期待: 右から AMARI（priority 100）、Mt（priority 200）の順に 2px 間隔で並び、重ならない。Materilune 側の暫定予約（52px）による二重の空白が無い。

## TC6: 縦位置の整合

- 前提: TC5 と同じ（同一行に AMARI と Mt が並ぶ状態）。
- 操作: ディスプレイスケール 100% で当該行を目視する。
- 期待: AMARI ボタンと Mt ボタンの上下位置が揃い、段差が見えない（旧実装は AMARI が 2px 上にずれていた）。125% / 150% でも上下 1px のぶれが出ない。

## TC7: 追加余白（ExtraOffset）の反映

- 前提: 任意。
- 操作: EditorPrefs の `AmariNoa.HierarchyButtons.ExtraOffset` に正の値を設定する（Materilune 導入時は Preferences の AmariNoa > Hierarchy Buttons ページで編集できる）。
- 期待: AMARI ボタン（と参加ツールのボタン列全体）が設定値のぶん左へ移動する。
