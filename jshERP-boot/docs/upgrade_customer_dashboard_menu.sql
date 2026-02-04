SET @funId := (SELECT `id` FROM `jsh_function` WHERE `url` = '/customer/dashboard' LIMIT 1);

INSERT INTO `jsh_function` (`number`,`name`,`parent_number`,`url`,`component`,`state`,`sort`,`enabled`,`type`,`push_btn`,`icon`,`delete_flag`)
SELECT '01020104','顾客看板','0102','/customer/dashboard','/customer/CustomerDashboard',b'0','0264',b'1','电脑版','', 'profile','0'
WHERE @funId IS NULL;

SET @funId := (SELECT `id` FROM `jsh_function` WHERE `url` = '/customer/dashboard' LIMIT 1);

UPDATE `jsh_user_business`
SET `value` = CONCAT(IFNULL(`value`, ''), CONCAT('[', @funId, ']'))
WHERE `type` = 'RoleFunctions'
  AND `key_id` IN ('4','10')
  AND (`value` IS NULL OR `value` NOT LIKE CONCAT('%[', @funId, ']%'));

SET @funId := (SELECT `id` FROM `jsh_function` WHERE `url` = '/customer/report' LIMIT 1);

INSERT INTO `jsh_function` (`number`,`name`,`parent_number`,`url`,`component`,`state`,`sort`,`enabled`,`type`,`push_btn`,`icon`,`delete_flag`)
SELECT '01020105','顾客报表','0102','/customer/report','/customer/CustomerReports',b'0','0265',b'1','电脑版','', 'profile','0'
WHERE @funId IS NULL;

SET @funId := (SELECT `id` FROM `jsh_function` WHERE `url` = '/customer/report' LIMIT 1);

UPDATE `jsh_user_business`
SET `value` = CONCAT(IFNULL(`value`, ''), CONCAT('[', @funId, ']'))
WHERE `type` = 'RoleFunctions'
  AND `key_id` IN ('4','10')
  AND (`value` IS NULL OR `value` NOT LIKE CONCAT('%[', @funId, ']%'));
