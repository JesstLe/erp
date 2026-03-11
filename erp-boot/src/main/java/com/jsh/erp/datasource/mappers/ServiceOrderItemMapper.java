package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.ServiceOrderItem;
import org.apache.ibatis.annotations.Param;

import java.util.Date;
import java.util.List;
import java.util.Map;

public interface ServiceOrderItemMapper {
    int deleteByPrimaryKey(Long id);

    int insert(ServiceOrderItem record);

    int insertSelective(ServiceOrderItem record);

    ServiceOrderItem selectByPrimaryKey(Long id);

    int updateByPrimaryKeySelective(ServiceOrderItem record);

    int updateByPrimaryKey(ServiceOrderItem record);

    List<ServiceOrderItem> selectByOrderId(@Param("serviceOrderId") Long serviceOrderId,
                                           @Param("tenantId") Long tenantId);

    List<Map<String, Object>> sumAmountByDepotAndTime(@Param("depotId") Long depotId,
                                                      @Param("startTime") Date startTime,
                                                      @Param("endTime") Date endTime,
                                                      @Param("tenantId") Long tenantId);
}

