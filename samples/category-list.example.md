# 分类名单示例（仅用于演示导入流程，不要在生产中使用）

> 本文件由项目维护者提供，**只包含保留的测试域名**（`.example.test` / `.invalid`），
> 用于验证「分类名单导入 → 违规内容拦截 → violation.html」这条链路。
> 正式的色情、赌博等类别名单必须由管理员提供并审核，程序与仓库都不会内置真实名单。
>
> 格式说明：
> - `## 类别名称` 开始一个类别，可重复出现多个类别。
> - `- 域名` 添加一条精确匹配规则。
> - `- 域名 include:subdomains` 或 `- *.域名` 添加一条子域匹配规则。
> - 无法规范化的行会被报告并跳过，不会被猜测或自动修正。

## 成人内容（示例）

- adult.example.test include:subdomains
- media.example.test include:subdomains

## 赌博内容（示例）

- gambling.example.test include:subdomains
- bet.example.test