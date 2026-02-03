package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.ServiceItem;
import org.apache.ibatis.annotations.Param;

import java.util.List;

public interface ServiceItemMapper {
    int deleteByPrimaryKey(Long id);

    int insert(ServiceItem record);

    int insertSelective(ServiceItem record);

    ServiceItem selectByPrimaryKey(Long id);

    int updateByPrimaryKeySelective(ServiceItem record);

    int updateByPrimaryKey(ServiceItem record);

    List<ServiceItem> selectByCondition(@Param("name") String name,
                                        @Param("enabled") Boolean enabled,
                                        @Param("tenantId") Long tenantId,
                                        @Param("offset") Integer offset,
                                        @Param("rows") Integer rows);

    Long countsByCondition(@Param("name") String name,
                           @Param("enabled") Boolean enabled,
                           @Param("tenantId") Long tenantId);
}

