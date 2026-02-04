package com.jsh.erp.datasource.mappers;

import com.jsh.erp.datasource.entities.InvoiceRequest;
import org.apache.ibatis.annotations.Param;

import java.util.List;

public interface InvoiceRequestMapper {
    int deleteByPrimaryKey(Long id);

    int insert(InvoiceRequest record);

    int insertSelective(InvoiceRequest record);

    InvoiceRequest selectByPrimaryKey(Long id);

    int updateByPrimaryKeySelective(InvoiceRequest record);

    int updateByPrimaryKey(InvoiceRequest record);

    List<InvoiceRequest> selectList(@Param("depotId") Long depotId,
                                    @Param("status") String status,
                                    @Param("keyword") String keyword,
                                    @Param("tenantId") Long tenantId,
                                    @Param("offset") Integer offset,
                                    @Param("rows") Integer rows);

    Long countList(@Param("depotId") Long depotId,
                   @Param("status") String status,
                   @Param("keyword") String keyword,
                   @Param("tenantId") Long tenantId);
}

