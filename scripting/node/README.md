`@tunelab/plugin` 发行布局：包体在 `tunelab/`（NODE_PATH 联调用），发布时 npm pack 该目录并把 require 名对齐 @tunelab/plugin。

本地联调：NODE_PATH 指 `scripting/node`，NODE_PATH 与 require('tunelab') 目录名匹配。
