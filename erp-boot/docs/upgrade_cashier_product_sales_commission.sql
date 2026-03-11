ALTER TABLE `jsh_cashier_session_product_item`
  ADD COLUMN `bar_code_snap` varchar(100) DEFAULT NULL COMMENT '条码快照' AFTER `material_name_snap`,
  ADD COLUMN `unit_snap` varchar(50) DEFAULT NULL COMMENT '单位快照' AFTER `bar_code_snap`,
  ADD COLUMN `sales_man_id` bigint(20) DEFAULT NULL COMMENT '销售员(经手人)id' AFTER `amount`,
  ADD COLUMN `commission_percent` decimal(24,6) DEFAULT '0.000000' COMMENT '提成比例(%)' AFTER `sales_man_id`,
  ADD COLUMN `commission_amount` decimal(24,6) DEFAULT '0.000000' COMMENT '提成金额' AFTER `commission_percent`;

