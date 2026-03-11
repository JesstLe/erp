package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.ServiceOrder;
import org.apache.ibatis.annotations.Param;

import java.util.List;

public interface ServiceOrderMapper {
    int deleteByPrimaryKey(Long id);

    int insert(ServiceOrder record);

    int insertSelective(ServiceOrder record);

    ServiceOrder selectByPrimaryKey(Long id);

    int updateByPrimaryKeySelective(ServiceOrder record);

    int updateByPrimaryKey(ServiceOrder record);

    List<ServiceOrder> selectBySessionId(@Param("sessionId") Long sessionId,
                                         @Param("tenantId") Long tenantId);

    ServiceOrder selectOpenDeskOrder(@Param("sessionId") Long sessionId,
                                     @Param("tenantId") Long tenantId);
}
