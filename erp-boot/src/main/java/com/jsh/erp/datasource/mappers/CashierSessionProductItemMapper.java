package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.CashierSessionProductItem;
import org.apache.ibatis.annotations.Param;

import java.util.List;

public interface CashierSessionProductItemMapper {
    int insertSelective(CashierSessionProductItem record);

    int updateByPrimaryKeySelective(CashierSessionProductItem record);

    CashierSessionProductItem selectByPrimaryKey(Long id);

    CashierSessionProductItem selectBySessionAndMaterial(@Param("sessionId") Long sessionId,
                                                         @Param("materialId") Long materialId,
                                                         @Param("tenantId") Long tenantId);

    List<CashierSessionProductItem> selectBySessionId(@Param("sessionId") Long sessionId,
                                                      @Param("tenantId") Long tenantId);
}
