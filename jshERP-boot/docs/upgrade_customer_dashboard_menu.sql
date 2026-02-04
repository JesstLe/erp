SET NAMES utf8mb4;

SET @funId := (SELECT `id` FROM `jsh_function` WHERE `url` = '/customer/dashboard' LIMIT 1);

INSERT INTO `jsh_function` (`number`,`name`,`parent_number`,`url`,`component`,`state`,`sort`,`enabled`,`type`,`push_btn`,`icon`,`delete_flag`)
SELECT '090003','顾客看板','0900','/customer/dashboard','/customer/CustomerDashboard',b'0','0103',b'1','电脑版','', 'profile','0'
WHERE @funId IS NULL;

SET @funId := (SELECT `id` FROM `jsh_function` WHERE `url` = '/customer/dashboard' LIMIT 1);

UPDATE `jsh_function`
SET `name` = '顾客看板',
    `number` = '090003',
    `parent_number` = '0900',
    `sort` = '0103',
    `icon` = 'profile'
WHERE `url` = '/customer/dashboard';

UPDATE `jsh_user_business`
SET `value` = CONCAT(IFNULL(`value`, ''), CONCAT('[', @funId, ']'))
WHERE `type` = 'RoleFunctions'
  AND `key_id` IN ('4','10')
  AND (`value` IS NULL OR `value` NOT LIKE CONCAT('%[', @funId, ']%'));

SET @funId := (SELECT `id` FROM `jsh_function` WHERE `url` = '/customer/report' LIMIT 1);

INSERT INTO `jsh_function` (`number`,`name`,`parent_number`,`url`,`component`,`state`,`sort`,`enabled`,`type`,`push_btn`,`icon`,`delete_flag`)
SELECT '090004','顾客报表','0900','/customer/report','/customer/CustomerReports',b'0','0104',b'1','电脑版','', 'profile','0'
WHERE @funId IS NULL;

SET @funId := (SELECT `id` FROM `jsh_function` WHERE `url` = '/customer/report' LIMIT 1);

UPDATE `jsh_function`
SET `name` = '顾客报表',
    `number` = '090004',
    `parent_number` = '0900',
    `sort` = '0104',
    `icon` = 'profile'
WHERE `url` = '/customer/report';

UPDATE `jsh_user_business`
SET `value` = CONCAT(IFNULL(`value`, ''), CONCAT('[', @funId, ']'))
WHERE `type` = 'RoleFunctions'
  AND `key_id` IN ('4','10')
  AND (`value` IS NULL OR `value` NOT LIKE CONCAT('%[', @funId, ']%'));
