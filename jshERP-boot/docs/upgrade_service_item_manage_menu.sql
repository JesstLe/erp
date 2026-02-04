INSERT IGNORE INTO `jsh_function` (`id`,`number`,`name`,`parent_number`,`url`,`component`,`state`,`sort`,`enabled`,`type`,`push_btn`,`icon`,`delete_flag`)
VALUES (264,'090002','服务项目管理','0900','/cashier/service_item','/cashier/ServiceItemList',b'0','0102',b'1','电脑版','1','profile','0');

UPDATE `jsh_user_business`
SET `value` = CONCAT(IFNULL(`value`, ''), '[264]')
WHERE `type` = 'RoleFunctions'
  AND `key_id` IN ('4','10')
  AND (`value` IS NULL OR `value` NOT LIKE '%[264]%');

UPDATE `jsh_user_business`
SET `btn_str` = CASE
  WHEN `btn_str` IS NULL OR `btn_str` = '' THEN '[{\"funId\":264,\"btnStr\":\"1\"}]'
  WHEN `btn_str` LIKE '%\"funId\":264%' THEN `btn_str`
  ELSE CONCAT(LEFT(`btn_str`, LENGTH(`btn_str`) - 1), ',{\"funId\":264,\"btnStr\":\"1\"}]')
END
WHERE `type` = 'RoleFunctions'
  AND `key_id` IN ('4','10');

