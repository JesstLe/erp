SET NAMES utf8mb4;

SET @fid := (SELECT `id` FROM `jsh_function` WHERE `url` = '/cashier' LIMIT 1);
UPDATE `jsh_user_business`
SET `value` = CONCAT(IFNULL(`value`, ''), CONCAT('[', @fid, ']'))
WHERE @fid IS NOT NULL
  AND `type` = 'RoleFunctions'
  AND IFNULL(`delete_flag`, '0') != '1'
  AND (`value` IS NULL OR `value` NOT LIKE CONCAT('%[', @fid, ']%'));

SET @fid := (SELECT `id` FROM `jsh_function` WHERE `url` = '/cashier/index' LIMIT 1);
UPDATE `jsh_user_business`
SET `value` = CONCAT(IFNULL(`value`, ''), CONCAT('[', @fid, ']'))
WHERE @fid IS NOT NULL
  AND `type` = 'RoleFunctions'
  AND IFNULL(`delete_flag`, '0') != '1'
  AND (`value` IS NULL OR `value` NOT LIKE CONCAT('%[', @fid, ']%'));

SET @fid := (SELECT `id` FROM `jsh_function` WHERE `url` = '/service/item/manage' LIMIT 1);
UPDATE `jsh_user_business`
SET `value` = CONCAT(IFNULL(`value`, ''), CONCAT('[', @fid, ']'))
WHERE @fid IS NOT NULL
  AND `type` = 'RoleFunctions'
  AND IFNULL(`delete_flag`, '0') != '1'
  AND (`value` IS NULL OR `value` NOT LIKE CONCAT('%[', @fid, ']%'));

SET @fid := (SELECT `id` FROM `jsh_function` WHERE `url` = '/customer/dashboard' LIMIT 1);
UPDATE `jsh_user_business`
SET `value` = CONCAT(IFNULL(`value`, ''), CONCAT('[', @fid, ']'))
WHERE @fid IS NOT NULL
  AND `type` = 'RoleFunctions'
  AND IFNULL(`delete_flag`, '0') != '1'
  AND (`value` IS NULL OR `value` NOT LIKE CONCAT('%[', @fid, ']%'));

SET @fid := (SELECT `id` FROM `jsh_function` WHERE `url` = '/customer/report' LIMIT 1);
UPDATE `jsh_user_business`
SET `value` = CONCAT(IFNULL(`value`, ''), CONCAT('[', @fid, ']'))
WHERE @fid IS NOT NULL
  AND `type` = 'RoleFunctions'
  AND IFNULL(`delete_flag`, '0') != '1'
  AND (`value` IS NULL OR `value` NOT LIKE CONCAT('%[', @fid, ']%'));
