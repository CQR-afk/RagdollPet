# Direction Pose 素材接口

BodyTurn 不使用整张 Body sprite 的 Z 旋转。当前 Stand 使用五套方向素材：

```text
Stand/Left
Stand/Left3Q
Stand/Center
Stand/Right3Q
Stand/Right
```

Left、Left3Q、Right3Q 与 Right 每套包含：

- `Body_Base.png`：躯干、腹部和固定下半身。
- `UpperBody_Base.png`：肩部、后颈和上胸，带隐藏重叠区。
- `Head_Base.png`：完整脸颊、下巴和隐藏鬃毛。
- `Neck_Fur.png`、`Chest_Fur.png`：前景接缝覆盖。
- `Ear_L.png`、`Ear_R.png`。
- `Eye_Base.png`、`Pupil_L.png`、`Pupil_R.png`、`Blink_Closed.png`。
- 与 Center 相同的 512×512 画布、地面线和颜色空间。

素材必须是真实方向 Pose，不得用 Center 整图 Z 旋转冒充。启用素材时将
`direction_poses.json` 对应 pose 的 `available` 改为 `true`，运行时资源接口才会允许渲染切换。

切换路径固定为 `Left ↔ Left3Q ↔ Center ↔ Right3Q ↔ Right`。任何反方向切换都只能逐级经过相邻 Pose。

## v1.15 Transition Clip

正式方向仍然只有上述五个。相邻状态间由 `direction_transitions.json` 定义四条可反向播放的 Clip：

```text
Center <-> Left3Q
Left3Q <-> Left
Center <-> Right3Q
Right3Q <-> Right
```

每条 Clip 预留 `Bridge01.png`、`Bridge02.png`。文件不存在时，日志会明确记录
`layer-stagger-placeholder`，运行时只使用来源/目标的分层错峰过渡，不把 placeholder 当作真实连续动画。
