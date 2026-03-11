package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.CashierSettlementPayment;
import org.apache.ibatis.annotations.Param;

import java.math.BigDecimal;
import java.util.List;

public interface CashierSettlementPaymentMapper {
    int deleteByPrimaryKey(Long id);

    int insert(CashierSettlementPayment record);

    int insertSelective(CashierSettlementPayment record);

    CashierSettlementPayment selectByPrimaryKey(Long id);

    int updateByPrimaryKeySelective(CashierSettlementPayment record);

    int updateByPrimaryKey(CashierSettlementPayment record);

    List<CashierSettlementPayment> selectBySettlementId(@Param("settlementId") Long settlementId, @Param("tenantId") Long tenantId);

    int deleteBySettlementId(@Param("settlementId") Long settlementId, @Param("tenantId") Long tenantId);

    BigDecimal sumCardAmountByMemberId(@Param("memberId") Long memberId, @Param("tenantId") Long tenantId);
}
