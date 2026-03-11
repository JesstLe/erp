SET @funId := (SELECT `id` FROM `jsh_function` WHERE `url` = '/cashier/service_item' LIMIT 1);

INSERT INTO `jsh_function` (`number`,`name`,`parent_number`,`url`,`component`,`state`,`sort`,`enabled`,`type`,`push_btn`,`icon`,`delete_flag`)
SELECT '090002','服务项目管理','0900','/cashier/service_item','/cashier/ServiceItemList',b'0','0102',b'1','电脑版','1','profile','0'
WHERE @funId IS NULL;

SET @funId := (SELECT `id` FROM `jsh_function` WHERE `url` = '/cashier/service_item' LIMIT 1);

UPDATE `jsh_user_business`
SET `value` = CONCAT(IFNULL(`value`, ''), CONCAT('[', @funId, ']'))
WHERE `type` = 'RoleFunctions'
  AND `key_id` IN ('4','10')
  AND (`value` IS NULL OR `value` NOT LIKE CONCAT('%[', @funId, ']%'));

UPDATE `jsh_user_business`
SET `btn_str` = CASE
  WHEN `btn_str` IS NULL OR `btn_str` = '' THEN CONCAT('[{\"funId\":', @funId, ',\"btnStr\":\"1\"}]')
  WHEN `btn_str` LIKE CONCAT('%\"funId\":', @funId, '%') THEN `btn_str`
  ELSE CONCAT(LEFT(`btn_str`, LENGTH(`btn_str`) - 1), CONCAT(',{\"funId\":', @funId, ',\"btnStr\":\"1\"}]'))
END
WHERE `type` = 'RoleFunctions'
  AND `key_id` IN ('4','10');
