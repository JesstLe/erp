SET @col := (
  SELECT COUNT(*)
  FROM information_schema.COLUMNS
  WHERE table_schema = DATABASE()
    AND table_name = 'jsh_supplier'
    AND column_name = 'create_time'
);

SET @sql := IF(
  @col = 0,
  'ALTER TABLE `jsh_supplier` ADD COLUMN `create_time` datetime NULL DEFAULT NULL',
  'SELECT 1'
);

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

UPDATE `jsh_supplier` SET `create_time` = IFNULL(`create_time`, NOW());
