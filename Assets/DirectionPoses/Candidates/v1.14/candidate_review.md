# v1.14 Stand Side DirectionPose 候选检查

`Stand_Left_03.png` 与 `Stand_Right_03.png` 已由用户确认，并作为 v1.14 Side Pose 的唯一拆层来源。其余候选继续保留用于追溯，没有接入 Runtime。

## 统一规格

- 画布：512 x 512 RGBA
- 注册基准：`HeadRig/Source/stand_assembled_preview.png`
- Alpha 可见区域顶部：约 Y=68
- 脚底基准：约 Y=498
- 生成方式：ImageGen identity-preserve 编辑模式
- 姿势目标：在已确认 3Q 基础上继续转向，约 55–65°，但不生成严格 90° 侧面

## 候选比较

| 候选 | 身份一致性 | 转向清晰度 | 结构/落地 | 备注 |
|---|---|---|---|---|
| Stand_Left_01 | 高 | 中 | 稳定 | 脸最接近正面参考，但与 Left3Q 的角度差较保守 |
| Stand_Left_02 | 高 | 中 | 稳定 | 身体轮廓干净，侧向变化最弱 |
| Stand_Left_03 | 中高 | 高 | 稳定 | 左向读取最清楚，适合检查五向层级；头部角度变化更明显 |
| Stand_Right_01 | 高 | 中高 | 稳定 | 身份稳定，右向变化适中 |
| Stand_Right_02 | 中高 | 高 | 稳定 | 身体和颈部右转关系自然，脸略小 |
| Stand_Right_03 | 中高 | 最高 | 稳定 | 右向层级最清楚，身体轮廓略变长 |

## 临时预览选择

- Left：`Stand_Left_03.png`
- Right：`Stand_Right_03.png`

以上选择已经确认。正式图层位于 `Assets/DirectionPoses/Stand/Left` 和 `Stand/Right`，并已接入五向 BodyTurn Runtime。

## 生成约束摘要

所有候选均同时参考：相邻已确认 3Q Pose、Center Pose、团团真实照片。提示词锁定同一只成年布偶猫、蓝眼、粉鼻、深色面罩和白色鼻梁、白色胸毛、灰褐色长毛、白腿、尾巴颜色与蓬松度；锁定画布、视觉尺寸、身体中心和脚底线。禁止镜像、Skew、整图旋转、透视拉伸、动作重设计、卡通化、背景和文字。
