UPDATE `jsh_depot`
SET `name` = CASE `name`
  WHEN '仓库1' THEN '门店1'
  WHEN '仓库2' THEN '门店2'
  WHEN '仓库3' THEN '门店3'
  ELSE `name`
END
WHERE `name` IN ('仓库1', '仓库2', '仓库3');

